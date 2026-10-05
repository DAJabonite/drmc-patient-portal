using System.Data;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class ImportLeases(IServiceScopeFactory scopes, ILogger<ImportLeases> logger)
{
    public string WorkerInstanceId { get; } = $"{Environment.MachineName[..Math.Min(Environment.MachineName.Length, 100)]}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<ImportLease?> ClaimAsync(Guid id, bool executionOnly, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.Id == id, token);
        if (batch is null || batch.Status is not (ImportStatus.Queued or ImportStatus.ValidationQueued or ImportStatus.ApprovalQueued) || executionOnly && batch.Status != ImportStatus.Queued) return null;
        var queued = batch.Status;
        var running = queued switch { ImportStatus.Queued => ImportStatus.Running, ImportStatus.ValidationQueued => ImportStatus.Validating, _ => ImportStatus.Approving };
        // Hold the live-execution range through claim so two processes cannot both observe an empty slot.
        await using var transaction = await db.Database.BeginTransactionAsync(queued == ImportStatus.Queued ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token);
        if (queued == ImportStatus.Queued && await db.ImportBatches.AnyAsync(row => row.Status == ImportStatus.Running && row.LeaseExpiresUtc > DateTime.UtcNow, token)) return null;
        var affected = await db.ImportBatches.Where(row => row.Id == id && row.Status == queued && row.RowVersion == batch.RowVersion && row.CreatedAtUtc > DateTime.UtcNow.AddDays(-7))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, running)
                .SetProperty(row => row.OwnerId, WorkerInstanceId)
                .SetProperty(row => row.StartedAtUtc, row => DateTime.UtcNow)
                .SetProperty(row => row.LeaseExpiresUtc, row => DateTime.UtcNow.AddSeconds(60)), token);
        if (affected != 1) return null;
        batch = await db.ImportBatches.AsNoTracking().SingleAsync(row => row.Id == id, token);
        await transaction.CommitAsync(token);
        logger.LogInformation("Import claim {BatchId} {Phase} {OwnerId}", id, running, WorkerInstanceId);
        return new ImportLease(this, batch, token);
    }

    internal async Task<byte[]> RenewAsync(ImportBatch batch, byte[] version, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.SetCommandTimeout(10);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var affected = await db.ImportBatches.Where(row => row.Id == batch.Id && row.Status == batch.Status && row.OwnerId == WorkerInstanceId && row.RowVersion == version && row.LeaseExpiresUtc > DateTime.UtcNow)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseExpiresUtc, row => DateTime.UtcNow.AddSeconds(60)), token);
        if (affected != 1) throw new ImportLeaseLostException();
        var renewed = await db.ImportBatches.Where(row => row.Id == batch.Id).Select(row => row.RowVersion).SingleAsync(token);
        await transaction.CommitAsync(token);
        logger.LogInformation("Import lease renewed {BatchId} {OwnerId}", batch.Id, WorkerInstanceId);
        return renewed;
    }

    public async Task RecoverAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A NULL lease is a pre-lease interrupted job, not evidence of a live owner.
        var expired = await db.ImportBatches.AsNoTracking().Where(row =>
            (row.Status == ImportStatus.Running || row.Status == ImportStatus.Validating || row.Status == ImportStatus.Approving) &&
            (row.LeaseExpiresUtc == null || row.LeaseExpiresUtc <= DateTime.UtcNow)).OrderBy(row => row.Id).Take(25).ToListAsync(token);
        foreach (var batch in expired)
        {
            var affected = await db.ImportBatches.Where(row => row.Id == batch.Id && row.Status == batch.Status && row.RowVersion == batch.RowVersion &&
                (row.LeaseExpiresUtc == null || row.LeaseExpiresUtc <= DateTime.UtcNow))
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, ImportStatus.Failed)
                    .SetProperty(row => row.FailureCode, "InterruptedAtRestart")
                    .SetProperty(row => row.CompletedAtUtc, row => DateTime.UtcNow), token);
            if (affected == 1) logger.LogInformation("Expired import lease failed without replay {BatchId}", batch.Id);
        }
    }

    internal void Lost(Guid id) => logger.LogWarning("Import lease lost; stopping phase {BatchId} {OwnerId}", id, WorkerInstanceId);
}

public sealed class ImportLeaseLostException : Exception;

public sealed class ImportLease : IAsyncDisposable
{
    private readonly ImportLeases owner;
    private readonly CancellationTokenSource phase;
    private readonly CancellationTokenSource stop;
    private readonly Task heartbeat;
    private byte[] version;
    public ImportBatch Batch { get; }
    public CancellationToken Token => phase.Token;

    internal ImportLease(ImportLeases owner, ImportBatch batch, CancellationToken token)
    {
        this.owner = owner; Batch = batch; version = batch.RowVersion;
        phase = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        heartbeat = RenewLoopAsync();
    }

    private async Task RenewLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stop.Token);
                version = await owner.RenewAsync(Batch, version, phase.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception)
        {
            owner.Lost(Batch.Id);
            await phase.CancelAsync();
        }
    }

    public async Task FinishAsync(ApplicationDbContext db, ImportStatus status, string? validationHash = null, int created = 0, string? failure = null)
    {
        await stop.CancelAsync();
        await heartbeat;
        Token.ThrowIfCancellationRequested();
        var affected = await db.ImportBatches.Where(row => row.Id == Batch.Id && row.Status == Batch.Status && row.OwnerId == Batch.OwnerId &&
            row.RowVersion == version && row.LeaseExpiresUtc > DateTime.UtcNow)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, status)
                .SetProperty(row => row.ValidationHash, validationHash ?? Batch.ValidationHash)
                .SetProperty(row => row.ApprovalHash, status == ImportStatus.Validated ? null : Batch.ApprovalHash)
                .SetProperty(row => row.ApprovedAtUtc, status == ImportStatus.Queued ? DateTime.UtcNow : Batch.ApprovedAtUtc)
                .SetProperty(row => row.CreatedCount, created)
                .SetProperty(row => row.FailureCode, failure)
                .SetProperty(row => row.CompletedAtUtc, status == ImportStatus.Succeeded || status == ImportStatus.Failed ? DateTime.UtcNow : (DateTime?)null)
                .SetProperty(row => row.LeaseExpiresUtc, (DateTime?)null), Token);
        if (affected != 1) throw new ImportLeaseLostException();
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync(); await heartbeat;
        stop.Dispose(); phase.Dispose();
    }
}
