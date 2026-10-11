using System.Collections.Concurrent;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

public sealed class RedStarReminderTests
{
    [Theory]
    [InlineData("2026-01-31", "2026-01-23", null)]
    [InlineData("2026-01-31", "2026-01-24", "SevenDaysBefore")]
    [InlineData("2026-01-31", "2026-01-30", "SevenDaysBefore")]
    [InlineData("2026-01-31", "2026-01-31", "DueToday")]
    [InlineData("2026-01-31", "2026-02-06", "DueToday")]
    [InlineData("2026-01-31", "2026-02-07", "SevenDaysOverdue")]
    [InlineData("2026-01-31", "2026-02-27", "SevenDaysOverdue")]
    [InlineData("2026-01-31", "2026-02-28", "Month:1")]
    [InlineData("2026-01-31", "2026-03-30", "Month:1")]
    [InlineData("2026-01-31", "2026-03-31", "Month:2")]
    [InlineData("2024-01-31", "2024-02-29", "Month:1")]
    [InlineData("2025-12-31", "2026-10-11", "Month:9")]
    public void Latest_milestone_handles_boundaries_calendar_months_and_restart_catch_up(string due, string today, string? expected)
    {
        Assert.Equal(expected, RedStarReminderService.LatestStage(DateTime.Parse(due), DateTime.Parse(today)));
    }

    [Theory]
    [InlineData("2026-10-10T23:59:00Z", "2026-10-11T00:00:00Z")]
    [InlineData("2026-10-11T00:00:00Z", "2026-10-12T00:00:00Z")]
    [InlineData("2026-10-11T15:00:00Z", "2026-10-12T00:00:00Z")]
    public void Daily_generation_uses_eight_am_in_Manila(string now, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), RedStarReminderService.NextDailyRun(DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void Email_template_contains_only_neutral_appointment_information_and_a_general_portal_link()
    {
        var (subject, body) = RedStarReminderService.EmailContent(new DateTime(2026, 10, 14), "https://portal.example.org/Patient");
        Assert.Contains("October 14, 2026", body);
        Assert.Contains("https://portal.example.org/Patient", body);
        foreach (var term in new[] { "Red Star", "RedStar", "HIV", "ARV", "PrEP", "PEP", "regimen", "patient code" })
            Assert.DoesNotContain(term, subject + body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DRMC", subject);
        Assert.Contains("&quot;", RedStarReminderService.EmailContent(DateTime.Today, "https://portal.example.org/Patient?x=\"test\"").Body);
    }

    [Fact]
    public void Production_reminders_require_explicit_enablement()
    {
        var defaults = new RedStarOptions();
        Assert.False(defaults.WorkerEnabled);
        Assert.False(defaults.RemindersEnabled);
        Assert.Equal(5, defaults.MaximumAttempts);
    }
}

[Collection(PortalCollection.Name)]
public sealed class RedStarReminderIntegrationTests(PortalFactory factory)
{
    private sealed class DeliveryAuditFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public string Subject { get; set; } = "";
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<RedStarAudit>().Any(e =>
                    e.State == EntityState.Added && e.Entity.Action == "ReminderDelivered" && e.Entity.Subject == Subject))
                throw new IOException("Synthetic private audit persistence failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Sender : IEmailSender
    {
        public bool Fail { get; set; }
        public ConcurrentQueue<(string Recipient, string Subject, string Body)> Sent { get; } = new();
        public Task SendEmailAsync(string recipient, string subject, string body)
        {
            if (Fail) throw new IOException("SYNTHETIC-PRIVATE-TRANSPORT-TEXT");
            Sent.Enqueue((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(int AppointmentId, int PatientId, int EnrollmentId, string UserId, string Email);

    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<Fixture> Create(DateTime due, bool optIn = true)
    {
        var email = $"reminder.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        return await Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var patient = new RedStarPatient { PatientCode = "FIXTURE-" + Guid.NewGuid().ToString("N"),
                FullName = "Synthetic private registry patient", PortalUserId = user.Id, EmailRemindersEnabled = optIn };
            var enrollment = new RedStarEnrollment { Patient = patient, Program = RedStarProgram.ART, Status = RedStarStatus.Active,
                Medicines = "SYNTHETIC-MEDICINE-NEVER-IN-EMAIL" };
            var appointment = new RedStarAppointment { Enrollment = enrollment, Kind = RedStarAppointmentKind.Refill,
                State = RedStarAppointmentState.Scheduled, DueDate = due, RemindersVerified = true };
            db.RedStarAppointments.Add(appointment);
            await db.SaveChangesAsync();
            return new Fixture(appointment.Id, patient.Id, enrollment.Id, user.Id, email);
        });
    }

    private static RedStarReminderService Service(IServiceProvider services, TestClock clock, int maximumAttempts = 5, bool enabled = true) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new RedStarOptions { RemindersEnabled = enabled, MaximumAttempts = maximumAttempts }),
            Options.Create(new PortalOptions { PublicOrigin = "https://portal.example.org" }),
            services.GetRequiredService<IHostEnvironment>(), clock, NullLogger<RedStarReminderService>.Instance);

    private async Task DispatchTargetAsync(RedStarReminderService service, int appointmentId, TestClock clock)
    {
        // Other tests share the synthetic database. Respect the production dispatcher's batch size
        // without assuming this fixture happens to be in the first batch of outstanding reminders.
        for (var batch = 0; batch < 50; batch++)
        {
            if (!await Db(db => db.RedStarReminders.AnyAsync(r => r.AppointmentId == appointmentId && r.Channel == RedStarReminderChannel.Email &&
                    (r.State == RedStarDeliveryState.Pending && r.NextAttemptUtc <= clock.Now ||
                     r.State == RedStarDeliveryState.Sending && r.LeaseExpiresUtc <= clock.Now)))) return;
            await service.DispatchAsync();
        }
        Assert.Fail("Synthetic fixture did not reach the dispatcher within 50 bounded batches.");
    }

    [Fact]
    public async Task Competing_generators_deduplicate_each_channel_and_encrypt_email_before_a_leased_send()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 1, 24, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 1, 31));
        var sender = new Sender();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var first = Service(host.Services, clock);
        var second = Service(host.Services, clock);
        await Task.WhenAll(first.GenerateAsync(), second.GenerateAsync());
        var messages = await Db(db => db.RedStarReminders.AsNoTracking().Where(r => r.AppointmentId == item.AppointmentId).ToListAsync());
        Assert.Equal(2, messages.Count);
        Assert.Equal(RedStarDeliveryState.Delivered, messages.Single(r => r.Channel == RedStarReminderChannel.InApp).State);
        var email = messages.Single(r => r.Channel == RedStarReminderChannel.Email);
        Assert.DoesNotContain(item.Email, Encoding.UTF8.GetString(email.ProtectedPayload!));
        await Task.WhenAll(first.DispatchAsync(), second.DispatchAsync());
        await DispatchTargetAsync(first, item.AppointmentId, clock);
        var sent = Assert.Single(sender.Sent, m => m.Recipient == item.Email);
        Assert.DoesNotContain("SYNTHETIC-MEDICINE", sent.Body);
        Assert.DoesNotContain("Red Star", sent.Subject + sent.Body);
        var delivered = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.Id == email.Id));
        Assert.Equal(RedStarDeliveryState.Delivered, delivered.State);
        Assert.Equal(1, delivered.Attempts);
        Assert.Null(delivered.ProtectedPayload);
        Assert.Null(delivered.LeaseOwner);
    }

    [Fact]
    public async Task Email_requires_opt_in_and_confirming_attendance_does_not_mark_pickup_or_stop_in_app_reminders()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 2, 1), false);
        await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.ConfirmedUtc, (DateTimeOffset?)clock.Now)));
        await Service(factory.Services, clock).GenerateAsync();
        var reminders = await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId).ToListAsync());
        Assert.Equal(RedStarReminderChannel.InApp, Assert.Single(reminders).Channel);
        Assert.Equal(RedStarAppointmentState.Scheduled, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.State).SingleAsync()));
        Assert.False(await Db(db => db.RedStarDispenses.AnyAsync(d => d.AppointmentId == item.AppointmentId)));
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("address")]
    [InlineData("unconfirmed")]
    [InlineData("locked")]
    [InlineData("unlink")]
    [InlineData("revision")]
    [InlineData("pickup")]
    [InlineData("completed")]
    [InlineData("unverified")]
    public async Task Delivery_rechecks_live_authority_and_cancels_obsolete_payloads(string change)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 3, 1));
        var sender = new Sender();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(a => a.Enrollment).ThenInclude(e => e.Patient)
                .ThenInclude(p => p.PortalUser).SingleAsync(a => a.Id == item.AppointmentId);
            var patient = appointment.Enrollment.Patient;
            switch (change)
            {
                case "consent": patient.EmailRemindersEnabled = false; break;
                case "address": patient.PortalUser!.Email = "new.synthetic@fixtures.test"; break;
                case "unconfirmed": patient.PortalUser!.EmailConfirmed = false; break;
                case "locked": patient.PortalUser!.LockoutEnabled = true; patient.PortalUser.LockoutEnd = clock.Now.AddDays(1); break;
                case "unlink": patient.PortalUserId = null; patient.PortalUser = null; break;
                case "revision": appointment.Revision++; appointment.DueDate = appointment.DueDate.AddDays(2); break;
                case "pickup": appointment.State = RedStarAppointmentState.PickedUp; break;
                case "completed": appointment.Enrollment.Status = RedStarStatus.Completed; break;
                case "unverified": appointment.RemindersVerified = false; break;
            }
            return await db.SaveChangesAsync();
        });
        await DispatchTargetAsync(service, item.AppointmentId, clock);
        Assert.DoesNotContain(sender.Sent, m => m.Recipient == item.Email);
        var cancelled = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
        Assert.Equal(RedStarDeliveryState.Cancelled, cancelled.State);
        Assert.Null(cancelled.ProtectedPayload);
        if (change == "consent")
            Assert.Equal(RedStarDeliveryState.Delivered, await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.InApp).Select(r => r.State).SingleAsync()));
    }

    [Fact]
    public async Task Restart_catch_up_sends_only_latest_milestone_and_discards_a_historical_pending_email()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 5, 1));
        var sender = new Sender();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        clock.Now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        await Service(host.Services, clock).GenerateAsync();
        await DispatchTargetAsync(service, item.AppointmentId, clock);
        Assert.Single(sender.Sent, m => m.Recipient == item.Email);
        var emails = await Db(db => db.RedStarReminders.AsNoTracking().Where(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email).ToListAsync());
        Assert.Equal(RedStarDeliveryState.Cancelled, emails.Single(r => r.Stage == "SevenDaysBefore").State);
        Assert.Equal(RedStarDeliveryState.Delivered, emails.Single(r => r.Stage == "Month:3").State);
        Assert.Equal(2, emails.Count);
    }

    [Fact]
    public async Task Expired_sending_lease_is_reclaimed_after_restart_and_transport_failures_have_bounded_retries()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 9, 1));
        var sender = new Sender();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var first = Service(host.Services, clock);
        var second = Service(host.Services, clock);
        await first.GenerateAsync();
        await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Sending)
                .SetProperty(r => r.LeaseOwner, "interrupted-worker").SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)clock.Now.AddMinutes(2))
                .SetProperty(r => r.Attempts, 1)));
        await first.DispatchAsync();
        Assert.DoesNotContain(sender.Sent, m => m.Recipient == item.Email);
        clock.Now = clock.Now.AddMinutes(3);
        await Task.WhenAll(first.DispatchAsync(), second.DispatchAsync());
        await DispatchTargetAsync(first, item.AppointmentId, clock);
        Assert.Single(sender.Sent, m => m.Recipient == item.Email);
        Assert.Equal(2, await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email).Select(r => r.Attempts).SingleAsync()));

        sender.Fail = true;
        var failedItem = await Create(new DateTime(2026, 9, 1));
        var retryService = Service(host.Services, clock, maximumAttempts: 2);
        await retryService.GenerateAsync();
        await DispatchTargetAsync(retryService, failedItem.AppointmentId, clock);
        var failed = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == failedItem.AppointmentId && r.Channel == RedStarReminderChannel.Email));
        Assert.Equal(1, failed.Attempts);
        clock.Now = failed.NextAttemptUtc.AddSeconds(1);
        await DispatchTargetAsync(retryService, failedItem.AppointmentId, clock);
        await retryService.DispatchAsync();
        failed = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.Id == failed.Id));
        Assert.Equal(2, failed.Attempts);
        Assert.Equal(RedStarDeliveryState.Failed, failed.State);
        Assert.Null(failed.ProtectedPayload);
    }

    [Fact]
    public async Task Disabled_reminders_and_unverified_imports_do_not_create_notifications()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 10, 1));
        await Service(factory.Services, clock, enabled: false).GenerateAsync();
        Assert.False(await Db(db => db.RedStarReminders.AnyAsync(r => r.AppointmentId == item.AppointmentId)));
        await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.RemindersVerified, false)));
        await Service(factory.Services, clock).GenerateAsync();
        Assert.False(await Db(db => db.RedStarReminders.AnyAsync(r => r.AppointmentId == item.AppointmentId)));
    }

    [Fact]
    public async Task Development_console_transport_fails_without_logging_a_private_recipient_or_body()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 10, 2));
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(new ConsoleEmailSender(NullLogger<ConsoleEmailSender>.Instance));
        }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        await DispatchTargetAsync(service, item.AppointmentId, clock);
        var email = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
        Assert.Equal(RedStarDeliveryState.Failed, email.State);
        Assert.Null(email.ProtectedPayload);
        Assert.NotNull(email.CompletedUtc);
    }

    [Fact]
    public async Task Account_deletion_detaches_delivery_and_retains_clinic_schedule_history()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 10, 3));
        var service = Service(factory.Services, clock);
        await service.GenerateAsync();
        await Db(db => db.Users.Where(u => u.Id == item.UserId).ExecuteDeleteAsync());
        await service.DispatchAsync();
        Assert.Null(await Db(db => db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.PortalUserId).SingleAsync()));
        Assert.True(await Db(db => db.RedStarAppointments.AnyAsync(a => a.Id == item.AppointmentId)));
        Assert.All(await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId).ToListAsync()), r =>
        {
            Assert.Null(r.PortalUserId);
            Assert.Null(r.ProtectedPayload);
            Assert.Equal(RedStarDeliveryState.Cancelled, r.State);
        });
    }

    [Fact]
    public async Task Delivery_completion_rolls_back_when_its_private_audit_cannot_persist_and_recovers_with_at_least_once_smtp()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 10, 4));
        var sender = new Sender();
        var auditFailure = new DeliveryAuditFailure();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(sender);
            s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(auditFailure));
        }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        var email = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
        auditFailure.Subject = email.Id.ToString("N");
        auditFailure.Armed = true;
        await DispatchTargetAsync(service, item.AppointmentId, clock);
        var pending = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.Id == email.Id));
        Assert.Equal(RedStarDeliveryState.Pending, pending.State);
        Assert.NotNull(pending.ProtectedPayload);
        Assert.Null(pending.CompletedUtc);
        Assert.False(await Db(db => db.RedStarAudits.AnyAsync(a => a.Subject == auditFailure.Subject && a.Action == "ReminderDelivered")));
        auditFailure.Armed = false;
        clock.Now = pending.NextAttemptUtc.AddSeconds(1);
        await DispatchTargetAsync(service, item.AppointmentId, clock);
        Assert.Equal(RedStarDeliveryState.Delivered, await Db(db => db.RedStarReminders.Where(r => r.Id == email.Id).Select(r => r.State).SingleAsync()));
        Assert.Equal(1, await Db(db => db.RedStarAudits.CountAsync(a => a.Subject == auditFailure.Subject && a.Action == "ReminderDelivered")));
        // SMTP cannot be rolled back when an acknowledgement or audit commit is interrupted.
        Assert.Equal(2, sender.Sent.Count(m => m.Recipient == item.Email));
    }
}
