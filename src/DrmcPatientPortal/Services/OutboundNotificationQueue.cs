using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class OutboundNotificationOptions
{
    public const string Section = "Notifications:Outbox";
    public bool WorkerEnabled { get; set; } = true;
    public int MaximumAttempts { get; set; } = 5;
    public int LifetimeMinutes { get; set; } = 60;
    public int RetentionDays { get; set; } = 7;
}

public sealed record OutboundNotificationPayload(string Recipient, string? Subject, string Body, string? PlainTextBody = null);

public interface IOutboundNotificationQueue
{
    void Stage(ApplicationDbContext db, Guid id, OutboundNotificationChannel channel, OutboundNotificationPayload payload,
        DateTimeOffset? expiresAtUtc = null, int? staffInvitationId = null);
    Task<Guid> EnqueueAsync(OutboundNotificationChannel channel, OutboundNotificationPayload payload, CancellationToken token = default);
    Task CancelInvitationsAsync(ApplicationDbContext db, int[] ids, CancellationToken token = default);
}

public sealed class OutboundNotificationQueue(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<OutboundNotificationOptions> options, TimeProvider clock) : IOutboundNotificationQueue
{
    public void Stage(ApplicationDbContext db, Guid id, OutboundNotificationChannel channel, OutboundNotificationPayload payload,
        DateTimeOffset? expiresAtUtc = null, int? staffInvitationId = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Recipient);
        ArgumentNullException.ThrowIfNull(payload.Body);
        if (channel == OutboundNotificationChannel.Email) ArgumentNullException.ThrowIfNull(payload.Subject);
        if (staffInvitationId is not null && channel != OutboundNotificationChannel.Email)
            throw new ArgumentException("Staff invitations require the email channel.", nameof(channel));
        var now = clock.GetUtcNow();
        db.OutboundNotifications.Add(new OutboundNotificationMessage
        {
            Id = id, Channel = channel, StaffInvitationId = staffInvitationId,
            CreatedAtUtc = now, NextAttemptAtUtc = now,
            ExpiresAtUtc = expiresAtUtc ?? now.AddMinutes(options.Value.LifetimeMinutes),
            PayloadProtected = protection.CreateProtector(KeyRingProtection.OutboundNotificationPurpose)
                .Protect(JsonSerializer.SerializeToUtf8Bytes(payload))
        });
    }

    public async Task<Guid> EnqueueAsync(OutboundNotificationChannel channel, OutboundNotificationPayload payload, CancellationToken token = default)
    {
        var id = Guid.NewGuid();
        await using var strategyScope = scopes.CreateAsyncScope();
        var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SqlReadRetry.RunIdempotentAsync(strategyDb, async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.OutboundNotifications.AnyAsync(m => m.Id == id, token)) return;
            Stage(db, id, channel, payload);
            await db.SaveChangesAsync(token);
        }, token);
        return id;
    }

    public async Task CancelInvitationsAsync(ApplicationDbContext db, int[] ids, CancellationToken token = default)
    {
        var now = clock.GetUtcNow();
        await db.OutboundNotifications.Where(m => m.StaffInvitationId != null && ids.Contains(m.StaffInvitationId.Value) &&
                (m.Status == OutboundNotificationStatus.Pending || m.Status == OutboundNotificationStatus.Sending))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, OutboundNotificationStatus.Cancelled)
                .SetProperty(m => m.PayloadProtected, (byte[]?)null).SetProperty(m => m.CompletedAtUtc, (DateTimeOffset?)now)
                .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)null), token);
    }
}

public sealed class OutboundNotificationDispatcher(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<OutboundNotificationOptions> options, TimeProvider clock, ILogger<OutboundNotificationDispatcher> logger)
{
    public async Task DispatchOnceAsync(CancellationToken token = default, Action? reportProgress = null)
    {
        void Progress()
        {
            token.ThrowIfCancellationRequested();
            reportProgress?.Invoke();
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await FinalizeAbandonedAsync(db, token);
        var now = clock.GetUtcNow();
        var ids = await SqlReadRetry.RunAsync(db, () => Due(db, now)
            .OrderBy(m => m.NextAttemptAtUtc).ThenBy(m => m.Id).Select(m => m.Id).Take(25).ToListAsync(token), token);
        Progress();
        foreach (var id in ids)
        {
            now = clock.GetUtcNow();
            // A fresh fencing token for every acquisition also protects concurrent calls on one service.
            var owner = Guid.NewGuid().ToString("N");
            var claimed = await Due(db, now).Where(m => m.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, OutboundNotificationStatus.Sending)
                    .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1).SetProperty(m => m.LeaseOwner, owner)
                    .SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)now.AddMinutes(2)), token);
            if (claimed != 1) { Progress(); continue; }
            var message = await db.OutboundNotifications.AsNoTracking().SingleOrDefaultAsync(m =>
                m.Id == id && m.Status == OutboundNotificationStatus.Sending && m.LeaseOwner == owner, token);
            if (message is null) { Progress(); continue; }
            try
            {
                var payload = JsonSerializer.Deserialize<OutboundNotificationPayload>(protection.CreateProtector(KeyRingProtection.OutboundNotificationPurpose)
                    .Unprotect(message.PayloadProtected ?? throw new InvalidDataException("Missing notification payload.")))
                    ?? throw new InvalidDataException("Invalid notification payload.");
                if (message.StaffInvitationId is { } invitationId)
                {
                    var invitation = await db.StaffInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invitationId, token);
                    if (invitation is null || invitation.RevokedAtUtc is not null || invitation.AcceptedAtUtc is not null ||
                        !string.Equals(invitation.Email, payload.Recipient, StringComparison.OrdinalIgnoreCase))
                    {
                        await CompleteAsync(db, message, OutboundNotificationStatus.Cancelled, token);
                        Progress();
                        continue;
                    }
                    if (new DateTimeOffset(DateTime.SpecifyKind(invitation.ExpiresAtUtc, DateTimeKind.Utc)) <= clock.GetUtcNow())
                    {
                        await CompleteAsync(db, message, OutboundNotificationStatus.Expired, token);
                        Progress();
                        continue;
                    }
                }
                var remaining = message.ExpiresAtUtc - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    await CompleteAsync(db, message, OutboundNotificationStatus.Expired, token);
                    Progress();
                    continue;
                }
                using var deadline = new CancellationTokenSource(remaining < TimeSpan.FromSeconds(30) ? remaining : TimeSpan.FromSeconds(30), clock);
                using var delivery = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                // SMTP/webhooks cannot participate in the SQL transaction: delivery is at least once.
                if (message.Channel == OutboundNotificationChannel.Email)
                {
                    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    if (sender is SmtpEmailSender smtp)
                        await smtp.SendEmailAsync(payload.Recipient, payload.Subject!, payload.Body, payload.PlainTextBody, delivery.Token);
                    else
                        await sender.SendEmailAsync(payload.Recipient, payload.Subject!, payload.Body).WaitAsync(delivery.Token);
                }
                else if (message.Channel == OutboundNotificationChannel.Sms)
                    await scope.ServiceProvider.GetRequiredService<ISmsSender>().SendSmsAsync(payload.Recipient, payload.Body, delivery.Token);
                else throw new InvalidDataException("Unknown notification channel.");
                await CompleteAsync(db, message, OutboundNotificationStatus.Delivered, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                var finished = clock.GetUtcNow();
                var terminal = message.AttemptCount >= options.Value.MaximumAttempts || message.ExpiresAtUtc <= finished;
                var status = message.ExpiresAtUtc <= finished ? OutboundNotificationStatus.Expired :
                    terminal ? OutboundNotificationStatus.DeadLetter : OutboundNotificationStatus.Pending;
                var changed = await Owned(db, message, finished).ExecuteUpdateAsync(set => set
                    .SetProperty(m => m.Status, status).SetProperty(m => m.LastError, error.GetType().Name)
                    .SetProperty(m => m.NextAttemptAtUtc, finished.Add(NotificationRetry.Delay(message.AttemptCount)))
                    .SetProperty(m => m.PayloadProtected, m => terminal ? null : m.PayloadProtected)
                    .SetProperty(m => m.CompletedAtUtc, terminal ? (DateTimeOffset?)finished : null)
                    .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)null), token);
                if (changed == 1)
                    logger.LogWarning("Outbound notification {MessageId} ({Channel}) {Status} after attempt {Attempt} ({ErrorType}).",
                        id, message.Channel, status, message.AttemptCount, error.GetType().Name);
            }
            // A persisted retry is worker progress even when the provider is unavailable.
            Progress();
        }
    }

    private IQueryable<OutboundNotificationMessage> Due(ApplicationDbContext db, DateTimeOffset now) =>
        db.OutboundNotifications.Where(m => m.ExpiresAtUtc > now && m.AttemptCount < options.Value.MaximumAttempts &&
            (m.Status == OutboundNotificationStatus.Pending && m.NextAttemptAtUtc <= now ||
             m.Status == OutboundNotificationStatus.Sending && m.LeaseExpiresAtUtc <= now));

    private static IQueryable<OutboundNotificationMessage> Owned(ApplicationDbContext db, OutboundNotificationMessage message, DateTimeOffset now) =>
        db.OutboundNotifications.Where(m => m.Id == message.Id && m.Status == OutboundNotificationStatus.Sending &&
            m.LeaseOwner == message.LeaseOwner && m.LeaseExpiresAtUtc > now && m.RowVersion == message.RowVersion);

    private async Task CompleteAsync(ApplicationDbContext db, OutboundNotificationMessage message, OutboundNotificationStatus status, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var changed = await Owned(db, message, now).ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, status)
            .SetProperty(m => m.PayloadProtected, (byte[]?)null).SetProperty(m => m.CompletedAtUtc, (DateTimeOffset?)now)
            .SetProperty(m => m.LastError, (string?)null).SetProperty(m => m.LeaseOwner, (string?)null)
            .SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)null), token);
        if (changed != 1)
            logger.LogWarning("Outbound notification {MessageId} ({Channel}) completion skipped because its lease changed.", message.Id, message.Channel);
    }

    private async Task FinalizeAbandonedAsync(ApplicationDbContext db, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var stale = await db.OutboundNotifications.AsNoTracking().Where(m =>
                (m.Status == OutboundNotificationStatus.Pending || m.Status == OutboundNotificationStatus.Sending && m.LeaseExpiresAtUtc <= now) &&
                (m.ExpiresAtUtc <= now || m.AttemptCount >= options.Value.MaximumAttempts))
            .OrderBy(m => m.NextAttemptAtUtc).ThenBy(m => m.Id).Take(100).ToListAsync(token);
        foreach (var message in stale)
        {
            var status = message.ExpiresAtUtc <= now ? OutboundNotificationStatus.Expired : OutboundNotificationStatus.DeadLetter;
            var changed = await db.OutboundNotifications.Where(m => m.Id == message.Id && m.RowVersion == message.RowVersion)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, status)
                    .SetProperty(m => m.PayloadProtected, (byte[]?)null).SetProperty(m => m.CompletedAtUtc, (DateTimeOffset?)now)
                    .SetProperty(m => m.LeaseOwner, (string?)null).SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)null), token);
            if (changed == 1)
                logger.LogWarning("Outbound notification {MessageId} ({Channel}) {Status} after attempt {Attempt}.",
                    message.Id, message.Channel, status, message.AttemptCount);
        }
    }

    public async Task CleanupAsync(CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await FinalizeAbandonedAsync(db, token);
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.RetentionDays);
        await db.OutboundNotifications.Where(m => m.CompletedAtUtc < cutoff).OrderBy(m => m.CompletedAtUtc)
            .ThenBy(m => m.Id).Take(100).ExecuteDeleteAsync(token);
    }
}

public sealed class OutboundNotificationWorker(OutboundNotificationDispatcher dispatcher, IOptions<OutboundNotificationOptions> options,
    TimeProvider clock, ILogger<OutboundNotificationWorker> logger, PortalWorkerHealth health) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await dispatcher.DispatchOnceAsync(stoppingToken, () => health.Success("outbound-notifications"));
                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await dispatcher.CleanupAsync(stoppingToken);
                    nextCleanup = clock.GetUtcNow().AddMinutes(5);
                }
                health.Success("outbound-notifications");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Outbound notification worker unavailable ({ErrorType}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
