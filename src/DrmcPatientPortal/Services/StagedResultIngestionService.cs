using System.Data;
using System.Security.Cryptography;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class StagedResultFileReadiness
{
    private readonly Dictionary<string, (long Length, long Written)> observed = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public bool IsReady(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return false;
        var current = (info.Length, info.LastWriteTimeUtc.Ticks);
        lock (observed)
        {
            var stable = observed.TryGetValue(path, out var previous) && previous == current;
            observed[path] = current;
            return stable;
        }
    }

    public void Forget(string path) { lock (observed) observed.Remove(path); }
    public void Prune()
    {
        lock (observed)
            foreach (var path in observed.Keys.Where(p => !File.Exists(p)).ToArray()) observed.Remove(path);
    }
}

public sealed class StagedResultIngestionService(IServiceScopeFactory scopes, IOptions<ResultStagingOptions> options,
    PortalWorkerHealth health, TimeProvider clock, ILogger<StagedResultIngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (await scope.ServiceProvider.GetRequiredService<StagedResultIngestionProcessor>().PollAsync(stoppingToken))
                        health.Success("result-staging");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                { logger.LogWarning("Result staging is unavailable ({FailureType}); claimed files remain retryable.", error.GetType().Name); }
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

// A scoped context owns the SQL session lock throughout one poll. Transactions and the context's
// tracked state are reset per claim. Audit outcomes are durable receipts for filesystem cleanup.
public sealed class StagedResultIngestionProcessor(ApplicationDbContext db, ILabReportStorage labs,
    IRadiologyReportStorage radiology, IAuditLogService audit, IStagedResultQuarantine quarantine,
    StagedResultFileReadiness readiness, IWebHostEnvironment environment, IOptions<ResultStagingOptions> options,
    TimeProvider clock, ILogger<StagedResultIngestionProcessor> logger)
{
    public const string IngestedAction = "STAGED_RESULT_INGESTED";
    public const string DuplicateAction = "STAGED_RESULT_DUPLICATE";
    public const string RejectedAction = "STAGED_RESULT_REJECTED";
    private const string LockName = "DRMC.ResultStaging";

    public async Task<bool> PollAsync(CancellationToken token = default)
    {
        if (!options.Value.Enabled) return true;
        await db.Database.OpenConnectionAsync(token);
        var locked = false;
        var healthy = true;
        try
        {
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'DRMC.ResultStaging', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
                var acquired = Convert.ToInt32(await command.ExecuteScalarAsync(token));
                if (acquired == -1) return true; // Another instance owns this poll.
                if (acquired < 0) throw new InvalidOperationException("Result staging lock could not be acquired.");
                locked = true;
            }
            foreach (var kind in Enum.GetValues<StagedResultKind>())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var root = new PrivateStorageRoot(environment, kind == StagedResultKind.Laboratory
                        ? options.Value.LaboratoryPath : options.Value.RadiologyPath);
                    Directory.CreateDirectory(root.Resolve("Processing"));
                    foreach (var directory in Entries(root, "Processing").Order(StringComparer.Ordinal))
                    {
                        var id = Path.GetFileName(directory);
                        if (!PrivateStorageRoot.IsGuid(id) || !Directory.Exists(directory)) continue;
                        var entries = Entries(root, "Processing", id).ToArray();
                        if (entries.Length == 0) { Directory.Delete(directory); continue; }
                        if (entries.Length != 1 || Directory.Exists(entries[0]))
                        { healthy = false; logger.LogWarning("Result staging claim {ClaimId} requires operator review.", id); continue; }
                        if (!await TryProcessAsync(root, kind, id, Path.GetFileName(entries[0]), token)) healthy = false;
                    }
                    foreach (var path in Entries(root).Order(StringComparer.Ordinal))
                    {
                        if (Directory.Exists(path) || !Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!readiness.IsReady(path)) continue;
                        // An open producer handle must not permit us to read an unfinished copy.
                        try { using var available = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); }
                        catch (IOException) { continue; }
                        var id = Guid.NewGuid().ToString("N");
                        var name = Path.GetFileName(path);
                        Directory.CreateDirectory(root.Resolve("Processing", id));
                        File.Move(path, root.Resolve("Processing", id, name));
                        readiness.Forget(path);
                        if (!await TryProcessAsync(root, kind, id, name, token)) healthy = false;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    healthy = false;
                    logger.LogWarning("Result staging directory {Kind} is unavailable ({FailureType}).", kind, error.GetType().Name);
                }
            }
            readiness.Prune();
            return healthy;
        }
        finally
        {
            try
            {
                if (locked && db.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    await using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'DRMC.ResultStaging', @LockOwner='Session';";
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private static IEnumerable<string> Entries(PrivateStorageRoot root, params string[] components) =>
        Directory.EnumerateFileSystemEntries(root.Resolve(components)).Select(Path.GetFileName)
            .Where(PrivateStorageRoot.IsLeaf).Select(name => root.Resolve([.. components, name!])).ToArray();

    private async Task<bool> TryProcessAsync(PrivateStorageRoot root, StagedResultKind kind, string id, string name, CancellationToken token)
    {
        try
        {
            // A reconnected SQL session has lost its lock. Never continue under that session.
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT APPLOCK_MODE('public', N'DRMC.ResultStaging', 'Session');";
                if (!string.Equals((string?)await command.ExecuteScalarAsync(token), "Exclusive", StringComparison.Ordinal))
                    throw new InvalidOperationException("Result staging lock was lost.");
            }
            var resource = $"StagedResult/{kind}/{id}";
            var outcome = await db.AuditLogs.AsNoTracking().Where(a => a.Resource == resource &&
                (a.Action == IngestedAction || a.Action == DuplicateAction || a.Action == RejectedAction)).Select(a => a.Action).SingleOrDefaultAsync(token);
            if (outcome is null)
            {
                // Recovered claims receive the same stability test as incoming files.
                var path = root.Resolve("Processing", id, name);
                if (!readiness.IsReady(path)) return true;
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                byte[]? content = null;
                string? rejection = null;
                if (source.Length is <= 0 or > LabReportStorage.MaximumBytes) rejection = "InvalidPdf";
                else
                {
                    content = await PrivateStorageIO.ReadBoundedAsync(source, LabReportStorage.MaximumBytes, token);
                    if (!content.AsSpan().StartsWith("%PDF-"u8)) rejection = "InvalidPdf";
                }
                outcome = await AttachAsync(kind, id, name, resource, content, rejection, token);
            }
            // The receipt has committed. No patient matching or writes are replayed on recovery.
            var claimed = root.Resolve("Processing", id, name);
            if (outcome == RejectedAction)
            {
                var target = new PrivateStorageRoot(environment, options.Value.QuarantinePath);
                Directory.CreateDirectory(target.Resolve(kind.ToString(), id));
                await quarantine.TransferAsync(claimed, target.Resolve(kind.ToString(), id, name), token);
            }
            else File.Delete(claimed);
            readiness.Forget(claimed);
            Directory.Delete(root.Resolve("Processing", id));
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning("Result staging claim {ClaimId} will retry ({FailureType}).", id, error.GetType().Name);
            return false;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<string> AttachAsync(StagedResultKind kind, string id, string name, string resource,
        byte[]? content, string? rejection, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        ParsedStagedResult? parsed = null;
        if (!StagedResultFilenameParser.TryParse(name, kind, DateOnly.FromDateTime(ClinicalClock.WallTime(clock.GetUtcNow())), out parsed, out var failure))
            rejection ??= failure;
        PatientRecord? patient = null;
        if (rejection is null)
        {
            var numbers = parsed!.IdentityCandidates.Select(c => c.HospitalNumber).ToArray();
            var patients = await db.PatientRecords.Where(p => p.HospitalNumber != null && numbers.Contains(p.HospitalNumber.Trim())).ToListAsync(token);
            var identities = parsed.IdentityCandidates.SelectMany(c => patients.Where(p =>
                string.Equals(p.HospitalNumber!.Trim(), c.HospitalNumber, StringComparison.OrdinalIgnoreCase)
                && PatientNames.MatchesStagedFullName(p, c.PatientFullName))).ToArray();
            if (identities.Length == 1) patient = identities[0];
            else rejection = identities.Length > 1 ? "AmbiguousIdentity" : patients.Count == 0 ? "UnknownHospitalNumber" : "PatientNameMismatch";
        }
        string action;
        string details;
        if (rejection is not null)
        {
            action = RejectedAction;
            details = $"Warning; reason={rejection}; kind={kind}; quarantine required.";
        }
        else
        {
            var date = parsed!.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var end = date.AddDays(1);
            var hash = Convert.ToHexString(SHA256.HashData(content!));
            await using var stream = new MemoryStream(content!, writable: false);
            var upload = new FormFile(stream, 0, stream.Length, "report", name)
            { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            var duplicate = false;
            var ambiguous = false;
            int recordId = 0;
            if (kind == StagedResultKind.Laboratory)
            {
                var results = (await db.LabResults.Where(r => r.PatientRecordId == patient!.Id && r.CollectedAt >= date && r.CollectedAt < end).ToListAsync(token))
                    .Where(r => string.Equals(PatientNames.Clean(r.TestName), parsed.TestName, StringComparison.OrdinalIgnoreCase)).ToArray();
                ambiguous = results.Length > 1;
                if (!ambiguous)
                {
                    var result = results.SingleOrDefault();
                    if (result is null)
                    {
                        result = new LabResult { PatientRecordId = patient!.Id, AccessionNumber = "STAGED-LAB-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                            TestName = parsed.TestName!, CollectedAt = date, Category = LabCategory.SpecialDiagnostics, PerformingUnit = "" };
                        db.LabResults.Add(result);
                        await db.SaveChangesAsync(token);
                    }
                    var previousHash = result.ReportSha256;
                    if (previousHash is null && result.ReportFileName is not null)
                        previousHash = Convert.ToHexString(SHA256.HashData(await labs.ReadAsync(result.Id, result.ReportFileName, token)));
                    duplicate = result.ReportFileName is not null && previousHash == hash;
                    if (duplicate) result.ReportSha256 = hash;
                    else
                    {
                        ReportAttachments.Apply(result, await labs.StoreAsync(result.Id, upload, token), clock.GetUtcNow().UtcDateTime);
                        result.Status = "Pending Verification"; result.ReleasedAt = null;
                    }
                    recordId = result.Id;
                }
            }
            else
            {
                var results = await db.RadiologyStudies.Where(r => r.PatientRecordId == patient!.Id && r.PerformedAt >= date && r.PerformedAt < end
                    && r.Modality == parsed.Modality).ToListAsync(token);
                ambiguous = results.Count > 1;
                if (!ambiguous)
                {
                    var result = results.SingleOrDefault();
                    if (result is null)
                    {
                        result = new RadiologyStudy { PatientRecordId = patient!.Id, AccessionNumber = "STAGED-RAD-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                            StudyName = parsed.Modality!.Value.DisplayName(), Modality = parsed.Modality.Value, PerformedAt = date };
                        db.RadiologyStudies.Add(result);
                        await db.SaveChangesAsync(token);
                    }
                    var previousHash = result.ReportSha256;
                    if (previousHash is null && result.ReportFileName is not null)
                        previousHash = Convert.ToHexString(SHA256.HashData(await radiology.ReadAsync(result.Id, result.ReportFileName, token)));
                    duplicate = result.ReportFileName is not null && previousHash == hash;
                    if (duplicate) result.ReportSha256 = hash;
                    else
                    {
                        ReportAttachments.Apply(result, await radiology.StoreAsync(result.Id, upload, token), clock.GetUtcNow().UtcDateTime);
                        result.Status = RadiologyStatus.PendingVerification; result.ReleasedAt = null;
                    }
                    recordId = result.Id;
                }
            }
            action = ambiguous ? RejectedAction : duplicate ? DuplicateAction : IngestedAction;
            details = ambiguous ? $"Warning; reason=AmbiguousResult; kind={kind}; quarantine required."
                : $"kind={kind}; recordId={recordId}; duplicate={duplicate}.";
        }
        // AuditLogService shares this context. The attachment and outcome either both commit or neither does.
        await audit.LogAsync(null, action, resource, details, "system");
        await transaction.CommitAsync(token);
        if (action == RejectedAction) logger.LogWarning("Result staging rejected claim {ClaimId} ({Kind}); see its security audit outcome.", id, kind);
        // Replaced ciphertext is left for approved, reference-aware retention, including uncertain commits.
        return action;
    }
}
