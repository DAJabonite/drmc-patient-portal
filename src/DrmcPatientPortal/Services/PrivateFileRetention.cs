using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class PrivateRetentionOptions
{
    public bool Enabled { get; set; }
    public string ApprovedPolicyId { get; set; } = "";
    public int OrphanGraceDays { get; set; } = 30;
}

// A file must first be observed without a database reference and remain unreferenced
// for the approved grace period. File age alone never starts the retention clock.
public sealed class PrivateFileRetention(IServiceScopeFactory scopes, IWebHostEnvironment environment,
    IOptions<PatientDocumentStorageOptions> documents, IOptions<LabReportStorageOptions> reports,
    IOptions<PrivateRetentionOptions> options, TimeProvider clock, ILogger<PrivateFileRetention> logger)
{
    private (int Kind, string Owner, string File)? cursor;

    public async Task RunAsync(CancellationToken token = default)
    {
        if (!options.Value.Enabled) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync(token);
        var locked = false;
        try
        {
            await using (var acquire = db.Database.GetDbConnection().CreateCommand())
            {
                acquire.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'DRMC.PrivateFileRetention', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
                if (Convert.ToInt32(await acquire.ExecuteScalarAsync(token)) < 0) return;
                locked = true;
            }
            var radiology = scope.ServiceProvider.GetRequiredService<IOptions<RadiologyReportStorageOptions>>();
            var roots = new[] { new PrivateStorageRoot(environment, documents.Value.RootPath), new PrivateStorageRoot(environment, reports.Value.RootPath),
                new PrivateStorageRoot(environment, radiology.Value.RootPath) };
            if (roots.Where((root, i) => roots.Skip(i + 1).Any(root.Overlaps)).Any())
                throw new InvalidOperationException("Patient, laboratory and radiology storage must be separate.");
            var processed = 0; var removed = 0;
            foreach (var candidate in Candidates(roots))
            {
                token.ThrowIfCancellationRequested();
                if (cursor is { } last && Compare(candidate, last) <= 0) continue;
                var (kind, owner, file) = candidate;
                var path = kind == 0 ? roots[0].Resolve(owner, file) : roots[kind].Resolve(file);
                var retirement = await db.PrivateFileRetirements.SingleOrDefaultAsync(x => x.Kind == kind && x.Owner == owner && x.FileName == file, token);
                var referenced = kind == 0
                    ? await db.PatientIdDocuments.AnyAsync(x => x.PatientUserId == owner && (x.FrontPhotoFileName == file || x.BackPhotoFileName == file), token)
                    : kind == 1 ? await db.LabResults.AnyAsync(x => x.ReportFileName == file, token)
                    : await db.RadiologyStudies.AnyAsync(x => x.ReportFileName == file, token);
                if (referenced)
                {
                    if (retirement is not null) db.PrivateFileRetirements.Remove(retirement);
                }
                else if (retirement is null)
                    db.PrivateFileRetirements.Add(new PrivateFileRetirement
                    { Id = Guid.NewGuid(), Kind = kind, Owner = owner, FileName = file, FirstUnreferencedUtc = clock.GetUtcNow() });
                else if (retirement.FirstUnreferencedUtc <= clock.GetUtcNow().AddDays(-options.Value.OrphanGraceDays))
                {
                    // A fresh reference query above fails closed on database errors. A long
                    // grace period protects in-flight writes and uncertain commit recovery.
                    File.Delete(path);
                    db.PrivateFileRetirements.Remove(retirement); removed++;
                }
                await db.SaveChangesAsync(token);
                db.ChangeTracker.Clear();
                cursor = candidate;
                if (++processed >= 100) break;
            }
            if (processed < 100) cursor = null;
            if (removed > 0) logger.LogInformation("Private retention policy {PolicyId} removed {FileCount} unreferenced files.", options.Value.ApprovedPolicyId, removed);
            // References whose files were already removed by a normal replacement are bounded too.
            var obsolete = await db.PrivateFileRetirements.OrderBy(x => x.Id).Take(100).ToListAsync(token);
            foreach (var item in obsolete)
            {
                if (item.Kind is < 0 or > 2) continue;
                var path = item.Kind == 0 ? roots[0].Resolve(item.Owner, item.FileName) : roots[item.Kind].Resolve(item.FileName);
                if (!File.Exists(path)) db.PrivateFileRetirements.Remove(item);
            }
            await db.SaveChangesAsync(token);
        }
        finally
        {
            try
            {
                if (locked)
                {
                    await using var release = db.Database.GetDbConnection().CreateCommand();
                    release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'DRMC.PrivateFileRetention', @LockOwner='Session';";
                    await release.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private static int Compare((int Kind, string Owner, string File) a, (int Kind, string Owner, string File) b)
    {
        var kind = a.Kind.CompareTo(b.Kind); if (kind != 0) return kind;
        var owner = StringComparer.Ordinal.Compare(a.Owner, b.Owner);
        return owner != 0 ? owner : StringComparer.Ordinal.Compare(a.File, b.File);
    }
    private static IEnumerable<(int Kind, string Owner, string File)> Candidates(PrivateStorageRoot[] roots)
    {
        foreach (var directory in roots[0].Entries().Order(StringComparer.Ordinal))
        {
            // Root-level files are not ID references (and may be a concurrent
            // readiness write probe). Leave unknown entries for operator review.
            if (!Directory.Exists(directory)) continue;
            var owner = Path.GetFileName(directory);
            foreach (var path in roots[0].Entries(owner).Order(StringComparer.Ordinal))
                if (!Directory.Exists(path) && PatientDocumentStorage.IsSafeFileName(Path.GetFileName(path)))
                    yield return (0, owner, Path.GetFileName(path));
        }
        for (var kind = 1; kind < roots.Length; kind++)
        foreach (var path in roots[kind].Entries().Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!Directory.Exists(path) && (PrivateStorageRoot.IsGuid(name) || name.EndsWith(".pdf", StringComparison.Ordinal) && PrivateStorageRoot.IsGuid(name[..^4])))
                yield return (kind, "", name);
        }
    }
}

public sealed class PrivateFileRetentionWorker(PrivateFileRetention retention, IOptions<PrivateRetentionOptions> options,
    PortalWorkerHealth health, ILogger<PrivateFileRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await retention.RunAsync(stoppingToken); health.Success("private-retention"); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Private retention failed ({ErrorType}).", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
