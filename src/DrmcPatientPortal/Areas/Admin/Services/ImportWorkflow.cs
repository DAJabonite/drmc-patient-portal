using System.Data;
using System.Security.Claims;
using System.Text.Json;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class ImportWorkflow(IServiceScopeFactory scopes, ImportStaging staging, AdminWrites writes, ILogger<ImportWorkflow> logger, ImportLeases leases)
{
    public Task<WriteResult> DryRunAsync(Guid id, ImportDryRun input, CancellationToken token, ClaimsPrincipal? actor = null) => writes.RunAsync(async db =>
    {
        var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id, token);
        if (!Editable(batch, input.RowVersion)) return WriteResult.Stale;
        if (input.Mappings.Count > 25 || input.Mappings.Select(mapping => mapping.SourcePatientKey).Distinct(StringComparer.Ordinal).Count() != input.Mappings.Count)
            return WriteResult.Invalid("Review only the source groups on this page; unknown or repeated mappings are not accepted.");
        var command = new ImportCommand(Guid.NewGuid().ToString("N"), batch!.FileHash, ImportTemplates.ValidationVersion, input.Mappings);
        await staging.SaveCommandAsync(id, command, token);
        batch.ValidationHash = ImportStaging.CommandHash(command); batch.ValidationVersion = ImportTemplates.ValidationVersion;
        batch.ApprovedById = actor?.FindFirstValue(ClaimTypes.NameIdentifier) ?? batch.ActorId;
        batch.ApprovedByEmail = null; batch.ApprovedAtUtc = null;
        batch.FailureCode = null; batch.CompletedAtUtc = null;
        batch.ApprovalHash = null; batch.Status = ImportStatus.ValidationQueued;
        await db.SaveChangesAsync(token);
        return WriteResult.Success;
    }, token);

    public Task<WriteResult> ApproveAsync(Guid id, ImportApproval input, ClaimsPrincipal actor, CancellationToken token) => writes.RunAsync(async db =>
    {
        var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id, token);
        if (!Editable(batch, input.RowVersion) || batch!.Status != ImportStatus.Validated) return WriteResult.Stale;
        if (!input.Confirm) return WriteResult.Invalid("Confirm the reviewed records and proposed counts before approval.");
        if (batch.ValidationVersion != ImportTemplates.ValidationVersion || batch.ValidationHash != input.Hash)
            return WriteResult.Invalid("The file, mappings or validation version changed. Validate and review the batch again.");
        var envelope = await staging.LoadAsync(id, token);
        if (!ImportStatusDisplay.VerifiedReport(batch, envelope))
            return WriteResult.Invalid("Review required. Resolve validation errors and validate the batch again before approval.");
        var account = await db.Users.SingleOrDefaultAsync(user => user.Id == actor.FindFirstValue(ClaimTypes.NameIdentifier), token);
        if (account is null) return WriteResult.Invalid("The approving staff account is unavailable.");
        batch.ApprovedById = account.Id; batch.ApprovedByEmail = account.Email ?? ""; batch.ApprovedAtUtc = null;
        batch.ApprovalHash = input.Hash; batch.Status = ImportStatus.ApprovalQueued;
        await db.SaveChangesAsync(token);
        return WriteResult.Success;
    }, token);

    public async Task<WriteResult> CancelAsync(Guid id, string rowVersion, CancellationToken token)
    {
        var result = await writes.RunAsync(async db =>
        {
            var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id, token);
            var version = AdminAudit.Version(rowVersion);
            if (batch is null || version is null || !batch.RowVersion.SequenceEqual(version)) return WriteResult.Stale;
            if (batch.Status is not (ImportStatus.Staged or ImportStatus.Validated or ImportStatus.Queued or ImportStatus.ValidationQueued or ImportStatus.ApprovalQueued or ImportStatus.Failed)) return WriteResult.Invalid("This batch is running or already finished.");
            batch.Status = ImportStatus.Cancelled; batch.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token); return WriteResult.Success;
        }, token);
        if (result.Succeeded) await PurgeAsync(id, token);
        return result;
    }

    private static bool Editable(ImportBatch? batch, string version) => batch is not null && batch.CreatedAtUtc > DateTime.UtcNow.AddDays(-7) &&
        batch.Status is ImportStatus.Staged or ImportStatus.Validated && AdminAudit.Version(version) is { } parsed && batch.RowVersion.SequenceEqual(parsed);

    public async Task ProcessAsync(Guid id, CancellationToken token)
    {
        await using var lease = await leases.ClaimAsync(id, false, token);
        if (lease is null) return;
        if (lease.Batch.Status == ImportStatus.Running) { await ExecuteClaimedAsync(lease); return; }
        await ValidateClaimedAsync(lease);
    }

    private async Task ValidateClaimedAsync(ImportLease lease)
    {
        var batch = lease.Batch; var id = batch.Id; var running = batch.Status; var token = lease.Token;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var actor = batch.ApprovedById is null ? null : await users.FindByIdAsync(batch.ApprovedById);
            if (actor is null || !actor.EmailConfirmed || !actor.TwoFactorEnabled || await users.IsLockedOutAsync(actor) || !await users.IsInRoleAsync(actor, "Admin"))
                throw new ImportRejectedException("ActorAccessRevoked");
            var envelope = await staging.LoadAsync(id, token);
            if (ImportStaging.FileHash(envelope.Content) != batch.FileHash || envelope.Template != batch.Template || batch.ValidationVersion != ImportTemplates.ValidationVersion)
                throw new ImportRejectedException("ValidationChanged");
            if (running == ImportStatus.Validating)
            {
                var command = await staging.LoadCommandAsync(id, token);
                if (command.FileHash != batch.FileHash || command.Version != ImportTemplates.ValidationVersion || ImportStaging.CommandHash(command) != batch.ValidationHash)
                    throw new ImportRejectedException("ValidationChanged");
                var groups = ImportValidation.Groups(ImportParser.Parse(envelope.Content, envelope.Format, envelope.Template)).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
                if (command.Mappings.Count > 25 || command.Mappings.Any(mapping => !groups.Contains(mapping.SourcePatientKey))) throw new ImportRejectedException("ValidationChanged");
                var submitted = command.Mappings.Select(mapping => mapping.SourcePatientKey).ToHashSet(StringComparer.Ordinal);
                envelope.Mappings.RemoveAll(mapping => submitted.Contains(mapping.SourcePatientKey)); envelope.Mappings.AddRange(command.Mappings);
            }
            else if (ImportStaging.ApprovalHash(envelope) != batch.ValidationHash || batch.ApprovalHash != batch.ValidationHash || envelope.Report?.Hash != batch.ValidationHash)
                throw new ImportRejectedException("ApprovalChanged");
            var plan = await ImportValidation.BuildAsync(db, envelope, token);
            // Validation tracks proposed rows for relationship fixup, but must never persist them.
            db.ChangeTracker.Clear();
            envelope.Report = plan.Report; await staging.SaveAsync(id, envelope, token);
            await lease.FinishAsync(db, running == ImportStatus.Approving && plan.Report.Valid ? ImportStatus.Queued : ImportStatus.Validated, plan.Report.Hash);
            await transaction.CommitAsync(token);
        }
        catch (ImportLeaseLostException) { logger.LogWarning("Import validation fence rejected {BatchId}", id); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await FailAsync(lease, error, "ValidationUnavailable");
        }
    }

    public async Task ExecuteAsync(Guid id, CancellationToken token)
    {
        await using var lease = await leases.ClaimAsync(id, true, token);
        if (lease is not null) await ExecuteClaimedAsync(lease);
    }

    private async Task ExecuteClaimedAsync(ImportLease lease)
    {
        var batch = lease.Batch; var id = batch.Id; var token = lease.Token;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var actor = batch.ApprovedById is null ? null : await users.FindByIdAsync(batch.ApprovedById);
            if (actor is null || !actor.EmailConfirmed || !actor.TwoFactorEnabled || await users.IsLockedOutAsync(actor) || !await users.IsInRoleAsync(actor, "Admin"))
                throw new ImportRejectedException("ActorAccessRevoked");
            var envelope = await staging.LoadAsync(id, token);
            if (ImportStaging.FileHash(envelope.Content) != batch.FileHash || envelope.Template != batch.Template || batch.ValidationVersion != ImportTemplates.ValidationVersion || ImportStaging.ApprovalHash(envelope) != batch.ApprovalHash || batch.ApprovalHash != batch.ValidationHash)
                throw new ImportRejectedException("ApprovalChanged");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            void Profile(string step) { logger.LogInformation("Import execution {Step} for batch {BatchId} took {ElapsedMilliseconds} ms", step, id, timer.ElapsedMilliseconds); timer.Restart(); }
            var plan = await ImportValidation.BuildAsync(db, envelope, token);
            Profile("BuildAndTrack");
            if (!plan.Report.Valid) throw new ImportRejectedException("ValidationChanged");
            foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToArray())
                entry.Property("Id").IsTemporary = true;
            Profile("PrepareKeys");
            await db.SaveChangesAsync(token);
            Profile("SaveBusiness");
            var detect = db.ChangeTracker.AutoDetectChangesEnabled;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                foreach (var node in plan.Nodes)
                {
                    var entry = db.Entry(node.Entity);
                    var fields = entry.Properties.Where(property => property.Metadata.Name is not "RowVersion" and not "ViewCount" and not "Id").Select(property => property.Metadata.Name);
                    db.AdminAuditLogs.Add(new AdminAuditLog
                    {
                        ActorId = batch.ApprovedById!, ActorEmail = batch.ApprovedByEmail!, TimestampUtc = DateTime.UtcNow,
                        Action = "ImportCreate", Entity = node.Template, RecordKey = entry.Property("Id").CurrentValue!.ToString()!,
                        SubjectPatientId = node.Patient?.Id, ChangedFields = JsonSerializer.Serialize(fields.Order().ToArray()),
                        NewValues = node.Template is "Doctors" or "PublicAdvisories" ? JsonSerializer.Serialize(AdminAudit.Values(db, node.Entity)) : null
                    });
                }
            }
            finally { db.ChangeTracker.AutoDetectChangesEnabled = detect; }
            Profile("BuildAudit");
            await db.SaveChangesAsync(token);
            Profile("SaveAudit");
            await lease.FinishAsync(db, ImportStatus.Succeeded, created: plan.Nodes.Count);
            Profile("Fence");
            await transaction.CommitAsync(token);
            Profile("Commit");
        }
        catch (ImportLeaseLostException) { logger.LogWarning("Import execution fence rejected {BatchId}", id); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            // The same fence also prevents replay or failure writes after an uncertain successful commit.
            await FailAsync(lease, error, "AtomicExecutionFailed");
        }
        if (!token.IsCancellationRequested) await PurgeAsync(id, token);
    }

    private async Task FailAsync(ImportLease lease, Exception error, string fallback)
    {
        if (lease.Token.IsCancellationRequested) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, lease.Token);
            var code = error is ImportRejectedException rejected && rejected.Message is "ApprovalChanged" or "ActorAccessRevoked" or "ValidationChanged" ? rejected.Message : fallback;
            await lease.FinishAsync(db, ImportStatus.Failed, failure: code);
            await transaction.CommitAsync(lease.Token);
        }
        catch (ImportLeaseLostException) { logger.LogWarning("Import failure fence rejected {BatchId}", lease.Batch.Id); }
        catch (OperationCanceledException) when (lease.Token.IsCancellationRequested) { }
    }

    public async Task RecoverAsync(CancellationToken token)
    {
        await leases.RecoverAsync(token);
        await MaintainAsync(token);
    }

    public async Task MaintainAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token))
        {
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var expired = await db.ImportBatches.Where(batch => batch.CreatedAtUtc <= cutoff && batch.Status != ImportStatus.Running && batch.Status != ImportStatus.Validating && batch.Status != ImportStatus.Approving && batch.Status != ImportStatus.Succeeded && batch.Status != ImportStatus.Cancelled && batch.Status != ImportStatus.Expired).OrderBy(batch => batch.Id).Take(25).ToListAsync(token);
            foreach (var batch in expired) { batch.Status = ImportStatus.Expired; batch.CompletedAtUtc = DateTime.UtcNow; }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        var purge = await db.ImportBatches.Where(batch => !batch.StagingPurged && (batch.Status == ImportStatus.Succeeded || batch.Status == ImportStatus.Cancelled || batch.Status == ImportStatus.Expired)).OrderBy(batch => batch.Id).Select(batch => batch.Id).Take(25).ToListAsync(token);
        foreach (var id in purge) await PurgeAsync(id, token);
        foreach (var id in staging.OldOrphans(DateTime.UtcNow.AddDays(-7)).Take(25))
            if (!await db.ImportBatches.AnyAsync(batch => batch.Id == id, token)) staging.Purge(id);
    }

    public async Task PurgeAsync(Guid id, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.ImportBatches.SingleOrDefaultAsync(batch => batch.Id == id, token);
        if (batch is null || batch.Status is not (ImportStatus.Succeeded or ImportStatus.Cancelled or ImportStatus.Expired)) return;
        staging.Purge(id); batch.StagingPurged = true; await db.SaveChangesAsync(token);
    }
}

public sealed class ImportWorker(IServiceScopeFactory scopes, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var workflow = scope.ServiceProvider.GetRequiredService<ImportWorkflow>();
                    await workflow.RecoverAsync(stoppingToken);
                    var id = await db.ImportBatches.Where(batch => batch.Status == ImportStatus.ValidationQueued || batch.Status == ImportStatus.ApprovalQueued ||
                        batch.Status == ImportStatus.Queued && !db.ImportBatches.Any(row => row.Status == ImportStatus.Running && row.LeaseExpiresUtc > DateTime.UtcNow))
                        .OrderBy(batch => batch.CreatedAtUtc).ThenBy(batch => batch.Id).Select(batch => (Guid?)batch.Id).FirstOrDefaultAsync(stoppingToken);
                    if (id.HasValue)
                    {
                        var phase = await db.ImportBatches.Where(batch => batch.Id == id.Value).Select(batch => batch.Status).SingleAsync(stoppingToken);
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        await workflow.ProcessAsync(id.Value, stoppingToken);
                        logger.LogInformation("Import phase {Phase} for batch {BatchId} completed in {ElapsedMilliseconds} ms", phase, id.Value, timer.ElapsedMilliseconds);
                    }
                    await workflow.MaintainAsync(stoppingToken);
                }
                catch (Exception error) when (error is not OperationCanceledException) { logger.LogWarning("Import processing is unavailable; no batch will be replayed automatically."); }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
