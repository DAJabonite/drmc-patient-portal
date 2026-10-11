using System.Globalization;
using System.Net;
using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class RedStarOptions
{
    // Clinic sign-off and deployment configuration must explicitly enable both switches.
    public bool WorkerEnabled { get; set; }
    public bool RemindersEnabled { get; set; }
    public int MaximumAttempts { get; set; } = 5;
}

public sealed class RedStarReminderService(IServiceScopeFactory scopes, IDataProtectionProvider protection,
    IOptions<RedStarOptions> options, IOptions<PortalOptions> portalOptions, IHostEnvironment environment,
    TimeProvider clock, ILogger<RedStarReminderService> logger)
{
    private const string ProtectionPurpose = "DRMC.PrivateAppointmentReminder.v1";
    private readonly string owner = Guid.NewGuid().ToString("N");
    private int MaximumAttempts => Math.Clamp(options.Value.MaximumAttempts, 1, 20);
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];
    private sealed record Payload(string Recipient, DateTime DueDate);
    private sealed record Candidate(int Id, int Revision, DateTime DueDate, string UserId, bool EmailOptIn,
        bool EmailConfirmed, string? Email);

    // Return the latest applicable milestone, including when a worker starts between milestones.
    // AddMonths always uses the original appointment date, so Jan 31 -> Feb 28 -> Mar 31.
    public static string? LatestStage(DateTime due, DateTime today)
    {
        due = due.Date;
        today = today.Date;
        if (today < due)
            return (due - today).Days <= 7 ? "SevenDaysBefore" : null;
        var months = (today.Year - due.Year) * 12 + today.Month - due.Month;
        if (months > 0 && due.AddMonths(months) > today) months--;
        if (months >= 1) return "Month:" + months.ToString(CultureInfo.InvariantCulture);
        return (today - due).Days >= 7 ? "SevenDaysOverdue" : "DueToday";
    }

    public static DateTimeOffset NextDailyRun(DateTimeOffset utc)
    {
        var local = ClinicalClock.WallTime(utc);
        var next = local.Date.AddHours(8);
        if (local >= next) next = next.AddDays(1);
        // Asia/Manila has no daylight-saving transitions.
        return new DateTimeOffset(next, TimeSpan.FromHours(8)).ToUniversalTime();
    }

    public async Task GenerateAsync(CancellationToken token = default)
    {
        if (!options.Value.RemindersEnabled) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        var today = ClinicalClock.WallTime(now).Date;
        await CancelObsoleteAsync(db, now, token);
        var lastId = 0;
        while (true)
        {
            var afterId = lastId;
            var candidates = await SqlReadRetry.RunAsync(db, () => Eligible(db, now)
                .Where(a => a.Id > afterId && a.DueDate <= today.AddDays(7)).OrderBy(a => a.Id).Take(100)
                .Select(a => new Candidate(a.Id, a.Revision, a.DueDate, a.Enrollment.Patient.PortalUserId!,
                    a.Enrollment.Patient.EmailRemindersEnabled, a.Enrollment.Patient.PortalUser!.EmailConfirmed,
                    a.Enrollment.Patient.PortalUser.Email)).ToListAsync(token), token);
            if (candidates.Count == 0) break;
            foreach (var candidate in candidates)
            {
                var stage = LatestStage(candidate.DueDate, today);
                if (stage is null) continue;
                await InsertAsync(db, candidate, stage, RedStarReminderChannel.InApp, now, token);
                if (candidate.EmailOptIn && candidate.EmailConfirmed && !string.IsNullOrWhiteSpace(candidate.Email))
                    await InsertAsync(db, candidate, stage, RedStarReminderChannel.Email, now, token);
            }
            lastId = candidates[^1].Id;
        }
    }

    private async Task InsertAsync(ApplicationDbContext db, Candidate candidate, string stage,
        RedStarReminderChannel channel, DateTimeOffset now, CancellationToken token)
    {
        if (await db.RedStarReminders.AnyAsync(r => r.AppointmentId == candidate.Id &&
            r.AppointmentRevision == candidate.Revision && r.Stage == stage && r.Channel == channel &&
            r.PortalUserId == candidate.UserId, token)) return;
        var reminder = new RedStarReminder
        {
            AppointmentId = candidate.Id, AppointmentRevision = candidate.Revision, Stage = stage,
            Channel = channel, PortalUserId = candidate.UserId, CreatedUtc = now, NextAttemptUtc = now,
            State = channel == RedStarReminderChannel.InApp ? RedStarDeliveryState.Delivered : RedStarDeliveryState.Pending,
            CompletedUtc = channel == RedStarReminderChannel.InApp ? now : null,
            ProtectedPayload = channel == RedStarReminderChannel.Email
                ? protection.CreateProtector(ProtectionPurpose).Protect(JsonSerializer.SerializeToUtf8Bytes(new Payload(candidate.Email!, candidate.DueDate)))
                : null
        };
        db.RedStarReminders.Add(reminder);
        db.RedStarAudits.Add(new RedStarAudit { ActorId = "system", Action = "ReminderCreated",
            Subject = reminder.Id.ToString("N"), TimestampUtc = now });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A competing worker won the database uniqueness constraint; neither channel is duplicated.
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private static IQueryable<RedStarAppointment> Eligible(ApplicationDbContext db, DateTimeOffset now) =>
        db.RedStarAppointments.AsNoTracking().Where(a => a.Kind == RedStarAppointmentKind.Refill &&
            a.State == RedStarAppointmentState.Scheduled && a.RemindersVerified &&
            a.Enrollment.Status == RedStarStatus.Active && a.Enrollment.Program != RedStarProgram.Unverified &&
            a.Enrollment.Patient.PortalUserId != null && a.Enrollment.Patient.PortalUser != null &&
            (!a.Enrollment.Patient.PortalUser.LockoutEnabled || a.Enrollment.Patient.PortalUser.LockoutEnd == null ||
             a.Enrollment.Patient.PortalUser.LockoutEnd <= now));

    private static Task<int> CancelObsoleteAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken token) =>
        db.RedStarReminders.Where(r =>
            (r.State == RedStarDeliveryState.Pending || r.State == RedStarDeliveryState.Sending ||
             r.Channel == RedStarReminderChannel.InApp && r.State == RedStarDeliveryState.Delivered) &&
            (r.PortalUserId == null || r.AppointmentRevision != r.Appointment.Revision ||
             r.PortalUserId != r.Appointment.Enrollment.Patient.PortalUserId ||
             r.Appointment.Kind != RedStarAppointmentKind.Refill || r.Appointment.State != RedStarAppointmentState.Scheduled ||
             !r.Appointment.RemindersVerified || r.Appointment.Enrollment.Status != RedStarStatus.Active ||
             r.Appointment.Enrollment.Program == RedStarProgram.Unverified ||
             r.Appointment.Enrollment.Patient.PortalUser == null ||
             r.Appointment.Enrollment.Patient.PortalUser.LockoutEnabled && r.Appointment.Enrollment.Patient.PortalUser.LockoutEnd > now ||
             r.Channel == RedStarReminderChannel.Email && (!r.Appointment.Enrollment.Patient.EmailRemindersEnabled ||
                 !r.Appointment.Enrollment.Patient.PortalUser.EmailConfirmed)))
        .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Cancelled)
            .SetProperty(r => r.ProtectedPayload, (byte[]?)null).SetProperty(r => r.CompletedUtc, (DateTimeOffset?)now)
            .SetProperty(r => r.LeaseOwner, (string?)null).SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);

    public async Task DispatchAsync(CancellationToken token = default)
    {
        if (!options.Value.RemindersEnabled) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        await CancelObsoleteAsync(db, now, token);
        await db.RedStarReminders.Where(r => r.Channel == RedStarReminderChannel.Email && r.Attempts >= MaximumAttempts &&
                (r.State == RedStarDeliveryState.Pending || r.State == RedStarDeliveryState.Sending && r.LeaseExpiresUtc <= now))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Failed)
                .SetProperty(r => r.ProtectedPayload, (byte[]?)null).SetProperty(r => r.CompletedUtc, (DateTimeOffset?)now)
                .SetProperty(r => r.LeaseOwner, (string?)null).SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);
        var ids = await SqlReadRetry.RunAsync(db, () => db.RedStarReminders.AsNoTracking()
            .Where(r => r.Channel == RedStarReminderChannel.Email && r.Attempts < MaximumAttempts &&
                (r.State == RedStarDeliveryState.Pending && r.NextAttemptUtc <= now ||
                 r.State == RedStarDeliveryState.Sending && r.LeaseExpiresUtc <= now))
            .OrderBy(r => r.NextAttemptUtc).ThenBy(r => r.Id).Select(r => r.Id).Take(25).ToListAsync(token), token);
        foreach (var id in ids)
        {
            now = clock.GetUtcNow();
            var claimed = await db.RedStarReminders.Where(r => r.Id == id && r.Attempts < MaximumAttempts &&
                    (r.State == RedStarDeliveryState.Pending && r.NextAttemptUtc <= now ||
                     r.State == RedStarDeliveryState.Sending && r.LeaseExpiresUtc <= now))
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Sending)
                    .SetProperty(r => r.Attempts, r => r.Attempts + 1).SetProperty(r => r.LeaseOwner, owner)
                    .SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)now.AddMinutes(2)), token);
            if (claimed != 1) continue;
            var reminder = await db.RedStarReminders.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.LeaseOwner == owner, token);
            if (reminder is null) continue;
            try
            {
                var payload = JsonSerializer.Deserialize<Payload>(protection.CreateProtector(ProtectionPurpose)
                    .Unprotect(reminder.ProtectedPayload ?? throw new InvalidDataException("Missing protected payload.")))
                    ?? throw new InvalidDataException("Invalid protected payload.");
                // Fresh read immediately before transport: queued consent, address and ownership are not authority.
                var current = await Eligible(db, clock.GetUtcNow()).Where(a => a.Id == reminder.AppointmentId &&
                        a.Revision == reminder.AppointmentRevision && a.Enrollment.Patient.PortalUserId == reminder.PortalUserId &&
                        a.Enrollment.Patient.EmailRemindersEnabled && a.Enrollment.Patient.PortalUser!.EmailConfirmed)
                    .Select(a => new { a.DueDate, a.Enrollment.Patient.PortalUser!.Email }).SingleOrDefaultAsync(token);
                if (current is null || !string.Equals(current.Email, payload.Recipient, StringComparison.OrdinalIgnoreCase) ||
                    current.DueDate.Date != payload.DueDate.Date ||
                    LatestStage(current.DueDate, ClinicalClock.WallTime(clock.GetUtcNow()).Date) != reminder.Stage)
                {
                    await CompleteAsync(db, id, RedStarDeliveryState.Cancelled, token);
                    continue;
                }
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                if (sender is ConsoleEmailSender)
                {
                    // The shared development transport logs full addresses/bodies. Never hand it private notifications.
                    logger.LogInformation("External appointment reminder delivery is unavailable with the development mail transport.");
                    await CompleteAsync(db, id, RedStarDeliveryState.Failed, token);
                    continue;
                }
                var content = EmailContent(payload.DueDate, PortalUrl());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                // A process can stop after SMTP accepted a message but before the lease is completed.
                // Reclaiming that lease gives at-least-once delivery, not an exactly-once SMTP guarantee.
                if (sender is SmtpEmailSender smtp)
                    await smtp.SendEmailAsync(payload.Recipient, content.Subject, content.Body, timeout.Token);
                else
                    await sender.SendEmailAsync(payload.Recipient, content.Subject, content.Body).WaitAsync(timeout.Token);
                await CompleteAsync(db, id, RedStarDeliveryState.Delivered, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // Provider exception text can contain an address or message; log only the exception type.
                logger.LogWarning("Appointment notification delivery failed ({ErrorType}), attempt {Attempt}.", error.GetType().Name, reminder.Attempts);
                var failed = reminder.Attempts >= MaximumAttempts;
                var finished = clock.GetUtcNow();
                await db.RedStarReminders.Where(r => r.Id == id && r.State == RedStarDeliveryState.Sending &&
                        r.LeaseOwner == owner && r.LeaseExpiresUtc > finished)
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, failed ? RedStarDeliveryState.Failed : RedStarDeliveryState.Pending)
                        .SetProperty(r => r.NextAttemptUtc, finished.Add(Backoff[Math.Min(reminder.Attempts - 1, Backoff.Length - 1)]))
                        .SetProperty(r => r.ProtectedPayload, r => failed ? null : r.ProtectedPayload)
                        .SetProperty(r => r.CompletedUtc, failed ? (DateTimeOffset?)finished : null)
                        .SetProperty(r => r.LeaseOwner, (string?)null).SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);
            }
        }
    }

    private async Task CompleteAsync(ApplicationDbContext db, Guid id, RedStarDeliveryState state, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var changed = await db.RedStarReminders.Where(r => r.Id == id && r.State == RedStarDeliveryState.Sending &&
                    r.LeaseOwner == owner && r.LeaseExpiresUtc > now)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, state).SetProperty(r => r.ProtectedPayload, (byte[]?)null)
                    .SetProperty(r => r.CompletedUtc, (DateTimeOffset?)now).SetProperty(r => r.LeaseOwner, (string?)null)
                    .SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);
            if (changed == 1)
            {
                db.RedStarAudits.Add(new RedStarAudit { ActorId = "system", Action = "Reminder" + state,
                    Subject = id.ToString("N"), TimestampUtc = now });
                await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private string PortalUrl()
    {
        var origin = portalOptions.Value.PublicOrigin;
        if (string.IsNullOrWhiteSpace(origin) && environment.IsDevelopment()) origin = "http://localhost:5095";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("Configure a trusted portal origin for appointment reminders.");
        return new Uri(uri, "/Patient").AbsoluteUri;
    }

    public static (string Subject, string Body) EmailContent(DateTime due, string portalUrl)
    {
        var date = due.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
        return ("DRMC appointment reminder", $"<p>DRMC reminder: you have a scheduled appointment on {WebUtility.HtmlEncode(date)}.</p>" +
            $"<p>Sign in to your patient portal for details: <a href=\"{WebUtility.HtmlEncode(portalUrl)}\">Open patient portal</a>.</p>" +
            "<p>If you have already attended or need to change the schedule, contact the clinic.</p>");
    }
}

public sealed class RedStarReminderWorker(RedStarReminderService reminders, IOptions<RedStarOptions> options,
    TimeProvider clock, ILogger<RedStarReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled || !options.Value.RemindersEnabled) return;
        var nextGeneration = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= nextGeneration)
                {
                    await reminders.GenerateAsync(stoppingToken);
                    nextGeneration = RedStarReminderService.NextDailyRun(clock.GetUtcNow());
                }
                await reminders.DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Appointment notification worker unavailable ({ErrorType}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
