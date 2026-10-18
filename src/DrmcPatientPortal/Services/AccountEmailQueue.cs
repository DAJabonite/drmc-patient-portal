using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class AccountEmailOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public int MaximumAttempts { get; set; } = 5;
    public int LifetimeMinutes { get; set; } = 60;
    public int RetentionDays { get; set; } = 7;
}

public interface IAccountEmailQueue
{
    void Stage(ApplicationDbContext db, Guid id, string userId, string recipient, string subject, string body);
    Task EnqueueAsync(string userId, string recipient, string subject, string body, CancellationToken token = default);
}

public sealed record AccountEmailPayload(string Recipient, string Subject, string Body);

public sealed class AccountEmailQueue(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<AccountEmailOptions> options, TimeProvider clock) : IAccountEmailQueue
{
    // Join the producer's transaction. The caller owns SaveChanges and commit.
    public void Stage(ApplicationDbContext db, Guid id, string userId, string recipient, string subject, string body)
    {
        var now = clock.GetUtcNow();
        var payload = protection.CreateProtector("DRMC.AccountEmail.v1").Protect(
            JsonSerializer.SerializeToUtf8Bytes(new AccountEmailPayload(recipient, subject, body)));
        db.AccountEmails.Add(new AccountEmail
        {
            Id = id, UserId = userId, ProtectedPayload = payload, CreatedUtc = now,
            ExpiresUtc = now.AddMinutes(options.Value.LifetimeMinutes), NextAttemptUtc = now
        });
    }

    public async Task EnqueueAsync(string userId, string recipient, string subject, string body, CancellationToken token = default)
    {
        var id = Guid.NewGuid();
        await using var strategyScope = scopes.CreateAsyncScope();
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SqlReadRetry.RunIdempotentAsync(strategyDb, async () =>
        {
            // Fresh tracker on each attempt; the same client-generated key verifies an uncertain insert.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.AccountEmails.AnyAsync(m => m.Id == id, token)) return;
            Stage(db, id, userId, recipient, subject, body);
            await db.SaveChangesAsync(token);
        }, token);
    }
}

public sealed class AccountEmailDispatcher(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<AccountEmailOptions> options, TimeProvider clock, ILogger<AccountEmailDispatcher> logger)
{
    public async Task DispatchOnceAsync(CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        // An interrupted send can have succeeded: SMTP delivery is at-least-once, never a clinical write.
        var ids = await SqlReadRetry.RunAsync(db, () => db.AccountEmails.AsNoTracking().Where(m => m.ExpiresUtc > now &&
            m.Attempts < options.Value.MaximumAttempts &&
            (m.Status == AccountEmailStatus.Pending && m.NextAttemptUtc <= now ||
             m.Status == AccountEmailStatus.Sending && m.LeaseExpiresUtc <= now))
            .OrderBy(m => m.NextAttemptUtc).ThenBy(m => m.Id).Select(m => m.Id).Take(25).ToListAsync(token), token);
        foreach (var id in ids)
        {
            now = clock.GetUtcNow();
            var owner = Guid.NewGuid().ToString("N");
            var claimed = await db.AccountEmails.Where(m => m.Id == id && m.ExpiresUtc > now && m.Attempts < options.Value.MaximumAttempts &&
                (m.Status == AccountEmailStatus.Pending && m.NextAttemptUtc <= now || m.Status == AccountEmailStatus.Sending && m.LeaseExpiresUtc <= now))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, AccountEmailStatus.Sending)
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1).SetProperty(m => m.LeaseOwner, owner)
                    .SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)now.AddMinutes(2)), token);
            if (claimed != 1) continue;
            var message = await db.AccountEmails.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.LeaseOwner == owner, token);
            if (message is null) continue;
            try
            {
                var payload = JsonSerializer.Deserialize<AccountEmailPayload>(protection.CreateProtector("DRMC.AccountEmail.v1")
                    .Unprotect(message.ProtectedPayload ?? throw new InvalidDataException("Missing notification payload.")))!;
                var remaining = message.ExpiresUtc - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero) throw new OperationCanceledException("Account message expired before delivery.");
                using var deadline = new CancellationTokenSource(remaining < TimeSpan.FromSeconds(30) ? remaining : TimeSpan.FromSeconds(30), clock);
                using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                if (sender is SmtpEmailSender smtp)
                    await smtp.SendEmailAsync(payload.Recipient, payload.Subject, payload.Body, deliveryTimeout.Token);
                else
                    await sender.SendEmailAsync(payload.Recipient, payload.Subject, payload.Body).WaitAsync(deliveryTimeout.Token);
                var completed = clock.GetUtcNow();
                var delivered = await db.AccountEmails.Where(m => m.Id == id && m.Status == AccountEmailStatus.Sending && m.LeaseOwner == owner && m.LeaseExpiresUtc > completed)
                    .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, AccountEmailStatus.Delivered)
                        .SetProperty(m => m.ProtectedPayload, (byte[]?)null).SetProperty(m => m.CompletedUtc, (DateTimeOffset?)completed)
                        .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)null), token);
                if (delivered != 1)
                    logger.LogWarning("Account email {MessageId} (Email) completion skipped because its lease changed.", id);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // Exception messages from SMTP providers may contain an address or the message itself.
                logger.LogWarning("Account email {MessageId} delivery failed ({ErrorType}), attempt {Attempt}.", id, error.GetType().Name, message.Attempts);
                var failed = message.Attempts >= options.Value.MaximumAttempts;
                var finished = clock.GetUtcNow();
                var expired = message.ExpiresUtc <= finished;
                var terminal = expired || failed;
                var status = expired ? AccountEmailStatus.Expired : failed ? AccountEmailStatus.Failed : AccountEmailStatus.Pending;
                var changed = await db.AccountEmails.Where(m => m.Id == id && m.Status == AccountEmailStatus.Sending && m.LeaseOwner == owner && m.LeaseExpiresUtc > finished)
                    .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, status)
                        .SetProperty(m => m.NextAttemptUtc, finished.Add(NotificationRetry.Delay(message.Attempts)))
                        .SetProperty(m => m.ProtectedPayload, m => terminal ? null : m.ProtectedPayload)
                        .SetProperty(m => m.CompletedUtc, terminal ? (DateTimeOffset?)finished : null)
                        .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)null), token);
                if (changed == 1 && terminal)
                    logger.LogWarning("Account email {MessageId} (Email) terminal status {Status} after attempt {Attempt} ({ErrorType}).", id, status, message.Attempts, error.GetType().Name);
            }
        }
    }

    public async Task CleanupAsync(CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        await db.AccountEmails.Where(m =>
            (m.Status == AccountEmailStatus.Pending && m.ExpiresUtc <= now) ||
            (m.Status == AccountEmailStatus.Sending && m.LeaseExpiresUtc <= now && m.ExpiresUtc <= now))
            .OrderBy(m => m.ExpiresUtc).ThenBy(m => m.Id).Take(100)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, AccountEmailStatus.Expired)
                .SetProperty(m => m.ProtectedPayload, (byte[]?)null).SetProperty(m => m.CompletedUtc, (DateTimeOffset?)now)
                .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)null), token);
        var exhausted = await db.AccountEmails.AsNoTracking().Where(m => m.ExpiresUtc > now && m.Attempts >= options.Value.MaximumAttempts &&
                (m.Status == AccountEmailStatus.Pending || m.Status == AccountEmailStatus.Sending && m.LeaseExpiresUtc <= now))
            .OrderBy(m => m.NextAttemptUtc).ThenBy(m => m.Id).Take(100).Select(m => new { m.Id, m.Attempts }).ToListAsync(token);
        foreach (var message in exhausted)
        {
            var changed = await db.AccountEmails.Where(m => m.Id == message.Id && m.ExpiresUtc > now && m.Attempts >= options.Value.MaximumAttempts &&
                    (m.Status == AccountEmailStatus.Pending || m.Status == AccountEmailStatus.Sending && m.LeaseExpiresUtc <= now))
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, AccountEmailStatus.Failed)
                    .SetProperty(m => m.ProtectedPayload, (byte[]?)null).SetProperty(m => m.CompletedUtc, (DateTimeOffset?)now)
                    .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)null), token);
            if (changed == 1)
                logger.LogWarning("Account email {MessageId} (Email) dead-lettered after interrupted attempt {Attempt}.", message.Id, message.Attempts);
        }
        var cutoff = now.AddDays(-options.Value.RetentionDays);
        await db.AccountEmails.Where(m => m.CompletedUtc < cutoff)
            .OrderBy(m => m.CompletedUtc).ThenBy(m => m.Id).Take(100).ExecuteDeleteAsync(token);
        await db.PortalSessions.Where(s => s.ExpiresUtc <= now)
            .OrderBy(s => s.ExpiresUtc).ThenBy(s => s.Id).Take(100).ExecuteDeleteAsync(token);
        await db.AuthenticatorConsumptions.Where(c => c.ExpiresUtc <= now)
            .OrderBy(c => c.ExpiresUtc).ThenBy(c => c.UserId).ThenBy(c => c.KeyFingerprint).ThenBy(c => c.TimeStep)
            .Take(100).ExecuteDeleteAsync(token);
    }
}

public sealed class AccountEmailWorker(AccountEmailDispatcher dispatcher, IOptions<AccountEmailOptions> options,
    TimeProvider clock, ILogger<AccountEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchOnceAsync(stoppingToken);
                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await dispatcher.CleanupAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow().AddMinutes(5);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Account email worker unavailable ({ErrorType}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
