using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class PortalSecurityCleanup(IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task CleanupAsync(CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        int removed;
        do
        {
            token.ThrowIfCancellationRequested();
            removed = await db.PortalSessions.Where(s => s.ExpiresUtc <= now)
                .OrderBy(s => s.ExpiresUtc).ThenBy(s => s.Id).Take(100).ExecuteDeleteAsync(token);
        } while (removed == 100);
        do
        {
            token.ThrowIfCancellationRequested();
            removed = await db.AuthenticatorConsumptions.Where(c => c.ExpiresUtc <= now)
                .OrderBy(c => c.ExpiresUtc).ThenBy(c => c.UserId).ThenBy(c => c.KeyFingerprint).ThenBy(c => c.TimeStep)
                .Take(100).ExecuteDeleteAsync(token);
        } while (removed == 100);
    }
}

// Retention must not depend on mail being enabled or a successful SMTP dispatch.
public sealed class PortalSecurityCleanupWorker(PortalSecurityCleanup cleanup, TimeProvider clock,
    ILogger<PortalSecurityCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await cleanup.CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Portal security cleanup failed ({ErrorType}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
