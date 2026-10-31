using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Controllers;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

public sealed class RedStarReminderTests
{
    [Theory]
    [InlineData(-35, RedStarRefillUrgency.Overdue)]
    [InlineData(-1, RedStarRefillUrgency.Overdue)]
    [InlineData(0, RedStarRefillUrgency.DueToday)]
    [InlineData(1, RedStarRefillUrgency.DueSoon)]
    [InlineData(7, RedStarRefillUrgency.DueSoon)]
    [InlineData(8, RedStarRefillUrgency.Scheduled)]
    public void Refill_urgency_uses_clinical_dates_and_the_inclusive_reminder_window(int offset, RedStarRefillUrgency expected)
    {
        var today = new DateTime(2026, 10, 27, 23, 59, 0);
        Assert.Equal(expected, RedStarReminderService.ClassifyRefill(today.Date.AddDays(offset), today));
    }

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
        var rendered = RedStarReminderService.RenderEmail(new DateTime(2026, 10, 14), "https://portal.example.org/Patient");
        Assert.Contains("October 14, 2026", rendered.PlainTextBody);
        Assert.Contains("Open patient portal:\nhttps://portal.example.org/Patient", rendered.PlainTextBody);
        Assert.Contains("cid:" + PortalEmailRenderer.SealContentId, rendered.HtmlBody);
        Assert.Equal(2, Regex.Matches(rendered.HtmlBody, "href=\"https://portal.example.org/Patient\"").Count);
        Assert.DoesNotContain("https://portal.example.org/Patient", WebUtility.HtmlDecode(Regex.Replace(rendered.HtmlBody, "<[^>]*>", string.Empty)));
        foreach (var term in new[] { "Red Star", "RedStar", "HIV", "ARV", "PrEP", "PEP", "regimen", "patient code" })
            Assert.DoesNotContain(term, rendered.PlainTextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shared_email_renderer_encodes_all_text_and_retains_literal_plain_text()
    {
        const string content = "<script>alert(\"synthetic\")</script> & Patient";
        const string url = "https://portal.example.org/Identity/Account/ConfirmEmail?code=a%2Bb&returnUrl=%2FPatient";
        var rendered = PortalEmailRenderer.Render(new(content, content, content, content, content, url, content, content,
            [new(content, IsCode: true)], Language: "fil", FallbackLabel: content));
        Assert.DoesNotContain("<script>", rendered.HtmlBody);
        Assert.Contains("&lt;script&gt;", rendered.HtmlBody);
        Assert.Contains("&amp;returnUrl=", rendered.HtmlBody);
        Assert.Contains(content, rendered.PlainTextBody);
        Assert.Contains(url, rendered.PlainTextBody);
        Assert.Contains("lang=\"fil\"", rendered.HtmlBody);
        Assert.Contains("role=\"presentation\"", rendered.HtmlBody);
        Assert.Contains("max-width:600px", rendered.HtmlBody);
    }

    [Theory]
    [InlineData("en", "Confirm email address")]
    [InlineData("fil", "Kumpirmahin ang email address")]
    [InlineData("ceb", "Kumpirmaha ang email address")]
    public void Shared_email_renderer_centers_brand_and_action_and_uses_localized_labels_without_exposing_the_url(string language, string actionText)
    {
        const string url = "https://portal.example.org/Identity/Account/ConfirmEmail?userId=synthetic-user&code=a%2Bb%2Fc%3D&returnUrl=%2FPatient%2FRadiology";
        var rendered = PortalEmailRenderer.Render(new("Subject", "Heading", "Preview", "Introduction", actionText, url,
            "Instructions", "Unexpected guidance", Language: language));
        var links = Regex.Matches(rendered.HtmlBody, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
        Assert.Equal(2, links.Count);
        Assert.All(links.Cast<Match>(), link =>
        {
            Assert.Equal(url, WebUtility.HtmlDecode(link.Groups[1].Value));
            Assert.Equal(actionText, WebUtility.HtmlDecode(Regex.Replace(link.Groups[2].Value, "<[^>]*>", string.Empty)));
        });
        Assert.Contains($"lang=\"{language}\"", rendered.HtmlBody);
        Assert.DoesNotContain(url, WebUtility.HtmlDecode(Regex.Replace(rendered.HtmlBody, "<[^>]*>", string.Empty)));
        Assert.Contains(actionText + ":\n" + url, rendered.PlainTextBody);

        var brandTable = Regex.Match(rendered.HtmlBody,
            $"<table\\b(?<attributes>[^>]*)>\\s*<tr>\\s*<td\\b[^>]*>\\s*<img\\b[^>]*src=\"cid:{PortalEmailRenderer.SealContentId}\"").Groups["attributes"].Value;
        var actionTable = Regex.Match(rendered.HtmlBody,
            "<table\\b(?<attributes>[^>]*)>\\s*<tr>\\s*<td\\b[^>]*>\\s*<a\\b").Groups["attributes"].Value;
        var contentCell = Regex.Match(rendered.HtmlBody,
            "<td\\b(?<attributes>[^>]*)>\\s*<h1\\b").Groups["attributes"].Value;
        Assert.Contains("align=\"center\"", brandTable);
        Assert.Matches("margin:\\s*0\\s+auto", brandTable);
        Assert.Contains("align=\"center\"", actionTable);
        Assert.Matches("margin:\\s*0\\s+auto", actionTable);
        Assert.Contains("align=\"left\"", contentCell);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,synthetic")]
    [InlineData("https://user:password@portal.example.org/Patient")]
    [InlineData("/Identity/Account/ConfirmEmail")]
    public void Shared_email_renderer_requires_an_absolute_web_action_without_credentials(string action)
    {
        Assert.Throws<ArgumentException>(() => PortalEmailRenderer.Render(new("Subject", "Heading", "Preview", "Intro",
            "Open portal", action, "Instructions", "Unexpected guidance")));
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

    private sealed class HeldSender(string recipient) : IEmailSender
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int attempts;

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            if (to != recipient) return;
            if (Interlocked.Increment(ref attempts) == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task;
            }
            else
            {
                SecondStarted.TrySetResult();
                await ReleaseSecond.Task;
            }
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
                .SingleAsync(a => a.Id == item.AppointmentId);
            var patient = appointment.Enrollment.Patient;
            var account = await db.Users.SingleAsync(u => u.Id == patient.PortalUserId);
            switch (change)
            {
                case "consent": patient.EmailRemindersEnabled = false; break;
                case "address": account.Email = "new.synthetic@fixtures.test"; break;
                case "unconfirmed": account.EmailConfirmed = false; break;
                case "locked": account.LockoutEnabled = true; account.LockoutEnd = clock.Now.AddDays(1); break;
                case "unlink": patient.PortalUserId = null; break;
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
    public async Task Transient_failure_survives_a_fresh_host_and_retries_only_when_due_with_the_same_message_id()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 9, 2));
        var failingSender = new Sender { Fail = true };
        Guid messageId;
        DateTimeOffset retryAt;
        using (var firstHost = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(failingSender); })))
        {
            var first = Service(firstHost.Services, clock);
            await first.GenerateAsync();
            await DispatchTargetAsync(first, item.AppointmentId, clock);
            var pending = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r =>
                r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
            Assert.Equal(RedStarDeliveryState.Pending, pending.State);
            Assert.Equal(1, pending.Attempts);
            Assert.NotNull(pending.ProtectedPayload);
            Assert.Null(pending.LeaseOwner);
            messageId = pending.Id;
            retryAt = pending.NextAttemptUtc;
        }

        var recoveredSender = new Sender();
        using var restartedHost = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(recoveredSender); }));
        var restarted = Service(restartedHost.Services, clock);
        await restarted.DispatchAsync();
        Assert.DoesNotContain(recoveredSender.Sent, m => m.Recipient == item.Email);
        clock.Now = retryAt;
        await DispatchTargetAsync(restarted, item.AppointmentId, clock);

        Assert.Single(recoveredSender.Sent, m => m.Recipient == item.Email);
        var delivered = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.Id == messageId));
        Assert.Equal(RedStarDeliveryState.Delivered, delivered.State);
        Assert.Equal(2, delivered.Attempts);
        Assert.Null(delivered.ProtectedPayload);
        Assert.Null(delivered.LeaseOwner);
    }

    [Fact]
    public async Task Transient_failures_use_the_shared_capped_exponential_retry_schedule()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 9, 3));
        var sender = new Sender { Fail = true };
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        var delays = new[] { TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(30) };
        for (var attempt = 1; attempt <= delays.Length; attempt++)
        {
            await DispatchTargetAsync(service, item.AppointmentId, clock);
            var pending = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r =>
                r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
            Assert.Equal(RedStarDeliveryState.Pending, pending.State);
            Assert.Equal(attempt, pending.Attempts);
            Assert.Equal(clock.Now.Add(delays[attempt - 1]), pending.NextAttemptUtc);
            clock.Now = pending.NextAttemptUtc;
        }
    }

    [Fact]
    public async Task An_old_attempt_cannot_complete_a_new_lease_acquired_by_the_same_service_instance()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
        var item = await Create(new DateTime(2026, 9, 4));
        var sender = new HeldSender(item.Email);
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var service = Service(host.Services, clock);
        await service.GenerateAsync();
        var first = DispatchTargetAsync(service, item.AppointmentId, clock);
        Task? second = null;
        try
        {
            await sender.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstOwner = await Db(db => db.RedStarReminders.Where(r =>
                r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email).Select(r => r.LeaseOwner).SingleAsync());
            clock.Now = clock.Now.AddMinutes(3);
            second = DispatchTargetAsync(service, item.AppointmentId, clock);
            await sender.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            sender.ReleaseFirst.TrySetResult();
            await first;
            var active = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r =>
                r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email));
            Assert.Equal(RedStarDeliveryState.Sending, active.State);
            Assert.Equal(2, active.Attempts);
            Assert.NotEqual(firstOwner, active.LeaseOwner);
            Assert.NotNull(active.ProtectedPayload);
            Assert.False(await Db(db => db.RedStarAudits.AnyAsync(a => a.Action == "ReminderDelivered" && a.Subject == active.Id.ToString("N"))));
        }
        finally
        {
            sender.ReleaseFirst.TrySetResult();
            sender.ReleaseSecond.TrySetResult();
            await first;
            if (second is not null) await second;
        }
        Assert.Equal(RedStarDeliveryState.Delivered, await Db(db => db.RedStarReminders.Where(r =>
            r.AppointmentId == item.AppointmentId && r.Channel == RedStarReminderChannel.Email).Select(r => r.State).SingleAsync()));
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


[Collection(PortalCollection.Name)]
public sealed class RedStarDemoTests(PortalFactory factory)
{
    private static string[] DemoIndexes(ApplicationDbContext db) => Enumerable.Range(1, 24)
        .Select(i => db.Phi.PatientCodeIndex($"RS-DEMO-{i:D3}")).ToArray();

    private static Task<List<RedStarEnrollment>> DemoEnrollments(ApplicationDbContext db)
    {
        var indexes = DemoIndexes(db);
        return db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).Include(e => e.Appointments).Include(e => e.Dispenses)
            .Where(e => indexes.Contains(e.Patient.PatientCodeBlindIndex!)).ToListAsync();
    }

    private static RedStarController Controller(ApplicationDbContext db, IServiceProvider services) =>
        new(db, services.GetRequiredService<AdminWrites>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "demo-test-staff")], "Test"))
                }
            }
        };

    [Fact]
    public async Task Fresh_demo_contains_all_refill_states_and_encrypted_private_fields()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var enrollments = await DemoEnrollments(db);
        Assert.Equal(24, enrollments.Count);
        Assert.Equal(16, enrollments.Count(e => e.Status == RedStarStatus.Active));
        Assert.Equal(8, enrollments.Count(e => e.Status == RedStarStatus.Deceased));
        var today = ClinicalClock.Today;
        Assert.Equal(5, enrollments.Count(e => e.Status == RedStarStatus.Deceased && e.StatusDate!.Value.Year == today.Year && e.StatusDate.Value.Month == today.Month));
        var scheduled = enrollments.SelectMany(e => e.Appointments).Where(a => a.State == RedStarAppointmentState.Scheduled).ToList();
        Assert.Equal(16, scheduled.Count);
        Assert.Equal(4, scheduled.Count(a => RedStarReminderService.ClassifyRefill(a.DueDate, today) == RedStarRefillUrgency.Overdue));
        Assert.Equal(2, scheduled.Count(a => RedStarReminderService.ClassifyRefill(a.DueDate, today) == RedStarRefillUrgency.DueToday));
        Assert.Equal(4, scheduled.Count(a => RedStarReminderService.ClassifyRefill(a.DueDate, today) == RedStarRefillUrgency.DueSoon));
        Assert.Equal(6, scheduled.Count(a => RedStarReminderService.ClassifyRefill(a.DueDate, today) == RedStarRefillUrgency.Scheduled));
        Assert.All(enrollments, enrollment =>
        {
            Assert.Equal(RedStarProgram.ART, enrollment.Program);
            Assert.True(enrollment.EnrolledDate <= enrollment.StartDate);
            var dispense = Assert.Single(enrollment.Dispenses);
            Assert.True(enrollment.StartDate <= dispense.DispensedDate);
            Assert.Equal(dispense.DispensedDate.AddDays(dispense.DaysSupplied!.Value), dispense.EstimatedSupplyEnd);
            Assert.Contains(enrollment.Appointments, a => a.Id == dispense.AppointmentId && a.State == RedStarAppointmentState.PickedUp);
            Assert.False(enrollment.Patient.EmailRemindersEnabled);
            if (enrollment.Status == RedStarStatus.Deceased)
            {
                Assert.DoesNotContain(enrollment.Appointments, a => a.State == RedStarAppointmentState.Scheduled);
                Assert.True(dispense.DispensedDate < enrollment.StatusDate);
                Assert.True(enrollment.StatusDate <= today);
            }
            else Assert.Equal(dispense.EstimatedSupplyEnd, Assert.Single(enrollment.Appointments, a => a.State == RedStarAppointmentState.Scheduled).DueDate);
        });
        var primary = Assert.Single(enrollments, e => e.Patient.PortalUserId != null);
        Assert.Equal("drmc-development-patient", primary.Patient.PortalUserId);
        Assert.Equal("RS-DEMO-001", primary.Patient.PatientCode);
        Assert.Equal(db.Phi.AccountIndex(primary.Patient.PortalUserId), primary.Patient.PortalUserIdBlindIndex);
        Assert.All(enrollments, e => Assert.Equal(db.Phi.PatientCodeIndex(e.Patient.PatientCode), e.Patient.PatientCodeBlindIndex));

        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT p.PatientCode, p.FullName, p.PortalUserId, e.Medicines, d.Medicines, p.PhiProtectionVersion, e.PhiProtectionVersion, d.PhiProtectionVersion FROM RedStarPatients p JOIN RedStarEnrollments e ON e.PatientId=p.Id JOIN RedStarDispenses d ON d.EnrollmentId=e.Id WHERE p.Id=@id";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = primary.PatientId; command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var expected = new[] { (nameof(RedStarPatient), "PatientCode", primary.Patient.PatientCode),
            (nameof(RedStarPatient), "FullName", primary.Patient.FullName),
            (nameof(RedStarPatient), "PortalUserId", primary.Patient.PortalUserId!),
            (nameof(RedStarEnrollment), "Medicines", primary.Medicines),
            (nameof(RedStarDispense), "Medicines", primary.Medicines) };
        for (var i = 0; i < expected.Length; i++)
        {
            var encrypted = reader.GetString(i);
            Assert.StartsWith(RedStarPhiProtection.Envelope, encrypted);
            Assert.Equal(expected[i].Item3, db.Phi.Unprotect(expected[i].Item1, expected[i].Item2, encrypted));
        }
        for (var i = 5; i < 8; i++) Assert.Equal(1, reader.GetInt32(i));
    }

    [Theory]
    [InlineData("2025-12-31T16:01:00Z")]
    [InlineData("2024-02-29T15:59:00Z")]
    [InlineData("2026-04-30T16:01:00Z")]
    public async Task Backfill_respects_clinical_month_boundaries_opt_out_and_staff_edits(string instant)
    {
        // Each case owns its database: deletion here simulates an older demo without Red Star fixtures.
        var isolated = new PortalFactory();
        try
        {
            await isolated.InitializeAsync();
            await using var scope = isolated.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var original = await DemoEnrollments(db);
            Assert.Equal(24, original.Count);
            var ids = original.Select(e => e.PatientId).ToArray();
            await db.RedStarDispenses.Where(d => ids.Contains(d.Enrollment.PatientId)).ExecuteDeleteAsync();
            await db.RedStarAppointments.Where(a => ids.Contains(a.Enrollment.PatientId)).ExecuteDeleteAsync();
            await db.RedStarEnrollments.Where(e => ids.Contains(e.PatientId)).ExecuteDeleteAsync();
            await db.RedStarPatients.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            var generalCount = await db.PatientRecords.CountAsync();
            var doctorCount = await db.Doctors.CountAsync();
            var passwordHash = await db.Users.Where(u => u.Id == "drmc-development-patient").Select(u => u.PasswordHash).SingleAsync();
            var clock = new TestClock(DateTimeOffset.Parse(instant));
            var disabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DevelopmentFixtures:Enabled"] = "false" }).Build();
            DbInitializer.Initialize(db, users, disabled, clock);
            Assert.Empty(await DemoEnrollments(db));
            DbInitializer.Initialize(db, users, configuration, clock);
            var seeded = await DemoEnrollments(db);
            Assert.Equal(24, seeded.Count);
            var today = ClinicalClock.WallTime(clock.Now).Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            Assert.Equal(5, seeded.Count(e => e.Status == RedStarStatus.Deceased && e.StatusDate >= monthStart && e.StatusDate <= today));
            Assert.Equal(3, seeded.Count(e => e.Status == RedStarStatus.Deceased && e.StatusDate < monthStart));
            Assert.All(seeded, e => Assert.True(e.StatusDate <= today));
            var edited = seeded.Single(e => e.Patient.PatientCode == "RS-DEMO-002");
            var tracked = await db.RedStarPatients.SingleAsync(p => p.Id == edited.PatientId);
            tracked.FullName = "Staff-maintained demo name";
            await db.SaveChangesAsync();
            var preservedVersion = tracked.RowVersion.ToArray();
            await db.RedStarAppointments.Where(a => a.EnrollmentId == edited.Id && a.State == RedStarAppointmentState.Scheduled).ExecuteDeleteAsync();
            var missing = seeded.Single(e => e.Patient.PatientCode == "RS-DEMO-024");
            await db.RedStarDispenses.Where(d => d.EnrollmentId == missing.Id).ExecuteDeleteAsync();
            await db.RedStarAppointments.Where(a => a.EnrollmentId == missing.Id).ExecuteDeleteAsync();
            await db.RedStarEnrollments.Where(e => e.Id == missing.Id).ExecuteDeleteAsync();
            await db.RedStarPatients.Where(p => p.Id == missing.PatientId).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            clock.Now = clock.Now.AddMonths(2);
            DbInitializer.Initialize(db, users, configuration, clock);
            clock.Now = clock.Now.AddMonths(1);
            DbInitializer.Initialize(db, users, configuration, clock);
            var backfilled = await DemoEnrollments(db);
            Assert.Equal(24, backfilled.Count);
            var preserved = backfilled.Single(e => e.Id == edited.Id);
            Assert.Equal("Staff-maintained demo name", preserved.Patient.FullName);
            Assert.Equal(preservedVersion, preserved.Patient.RowVersion);
            Assert.DoesNotContain(preserved.Appointments, a => a.State == RedStarAppointmentState.Scheduled);
            Assert.All(seeded.Where(e => e.Id != missing.Id), before => Assert.Equal(before.StatusDate, backfilled.Single(e => e.Id == before.Id).StatusDate));
            Assert.Equal(generalCount, await db.PatientRecords.CountAsync());
            Assert.Equal(doctorCount, await db.Doctors.CountAsync());
            Assert.Equal(passwordHash, await db.Users.Where(u => u.Id == "drmc-development-patient").Select(u => u.PasswordHash).SingleAsync());
        }
        finally { await ((IAsyncLifetime)isolated).DisposeAsync(); }
    }

    [Fact]
    public async Task Seeded_patient_and_staff_pages_show_due_warnings_with_workers_disabled_and_filter_expired_months()
    {
        Assert.False(factory.Services.GetRequiredService<IOptions<RedStarOptions>>().Value.WorkerEnabled);
        Assert.False(factory.Services.GetRequiredService<IOptions<RedStarOptions>>().Value.RemindersEnabled);
        var clinic = await StaffAccounts.SignInAsync(factory, "redstar.demo@fixtures.test", "RedStarStaff");
        var registry = await clinic.GetStringAsync("/Admin/RedStar?search=RS-DEMO");
        foreach (var text in new[] { "Overdue replenishment", "Replenishment due today", "Replenishment due soon", "Refill scheduled", "Current Active / LIVING" })
            Assert.Contains(text, registry);
        var deliveries = await clinic.GetStringAsync("/Admin/RedStar/Deliveries");
        Assert.Contains("Current medicine replenishment", deliveries);
        Assert.Contains("Overdue replenishment", deliveries);
        var patient = factory.CreatePortalClient();
        await patient.SignInSuccessfullyAsync(PortalFactory.PrimaryEmail, PortalFactory.Password);
        var medications = await patient.GetStringAsync("/Patient/Medications");
        Assert.Contains("Medicine replenishment reminder", medications);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seeded = await DemoEnrollments(db);
        var primary = seeded.Single(e => e.Patient.PatientCode == "RS-DEMO-001");
        var due = primary.Appointments.Single(a => a.State == RedStarAppointmentState.Scheduled).DueDate;
        Assert.Contains(due.ToString("MMMM d, yyyy"), medications);
        Assert.Contains(primary.Medicines, medications);
        var empty = factory.CreatePortalClient();
        await empty.SignInSuccessfullyAsync(PortalFactory.EmptyEmail, PortalFactory.Password);
        Assert.DoesNotContain("Medicine replenishment reminder", await empty.GetStringAsync("/Patient/Medications"));
        var month = ClinicalClock.Today.ToString("yyyy-MM");
        var expired = await clinic.GetStringAsync("/Admin/RedStar?worklist=expired&search=RS-DEMO&month=" + month);
        foreach (var enrollment in seeded.Where(e => e.Status == RedStarStatus.Deceased))
        {
            if (enrollment.StatusDate!.Value.ToString("yyyy-MM") == month) Assert.Contains(enrollment.Patient.PatientCode, expired);
            else Assert.DoesNotContain(enrollment.Patient.PatientCode, expired);
        }
        var prior = seeded.Where(e => e.Status == RedStarStatus.Deceased).Min(e => e.StatusDate)!.Value.ToString("yyyy-MM");
        var previous = await clinic.GetStringAsync("/Admin/RedStar?worklist=expired&search=RS-DEMO&month=" + prior);
        Assert.Contains(seeded.Single(e => e.StatusDate?.ToString("yyyy-MM") == prior).Patient.PatientCode, previous);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await clinic.GetAsync("/Admin/RedStar?month=2026-13")).StatusCode);
    }

    [Fact]
    public async Task Refill_worklists_ignore_other_care_dates_and_closed_schedules_and_identify_unverified_dates()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = Controller(db, scope.ServiceProvider);
        var baseline = Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(await controller.Index(search: null)).Model);
        var before = Assert.IsType<RedStarDeliveriesPage>(Assert.IsType<ViewResult>(await controller.Deliveries()).Model);
        var prefix = "REFILL-VIS-" + Guid.NewGuid().ToString("N");
        var today = ClinicalClock.Today;
        var scenarios = new[] {
            (RedStarAppointmentKind.Lab, RedStarAppointmentState.Scheduled, RedStarStatus.Active, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Consultation, RedStarAppointmentState.Scheduled, RedStarStatus.Active, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.PickedUp, RedStarStatus.Active, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Cancelled, RedStarStatus.Active, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Scheduled, RedStarStatus.Deceased, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Scheduled, RedStarStatus.Discontinued, RedStarProgram.ART, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Scheduled, RedStarStatus.Active, RedStarProgram.Unverified, 0, true),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Scheduled, RedStarStatus.Active, RedStarProgram.ART, 1, false),
            (RedStarAppointmentKind.Refill, RedStarAppointmentState.Scheduled, RedStarStatus.Active, RedStarProgram.ART, -1, true)
        };
        var number = 0;
        foreach (var (kind, state, status, program, offset, verified) in scenarios)
            db.RedStarAppointments.Add(new RedStarAppointment
            {
                Enrollment = new RedStarEnrollment
                {
                    Patient = new RedStarPatient { PatientCode = prefix + (++number), FullName = "Synthetic reminder visibility" },
                    Program = program, Status = status, StatusDate = today
                },
                Kind = kind, State = state, DueDate = today.AddDays(offset), RemindersVerified = verified
            });
        await db.SaveChangesAsync();
        var dueToday = Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(await controller.Index(search: prefix, worklist: "today")).Model);
        Assert.Empty(dueToday.Rows);
        Assert.Equal(baseline.Counts["today"], dueToday.Counts["today"]);
        Assert.Equal(baseline.Counts["week"] + 1, dueToday.Counts["week"]);
        Assert.Equal(baseline.Counts["overdue"] + 1, dueToday.Counts["overdue"]);
        var soon = Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(await controller.Index(search: prefix, worklist: "week")).Model);
        Assert.False(Assert.Single(soon.Rows).NextRefillVerified);
        var after = Assert.IsType<RedStarDeliveriesPage>(Assert.IsType<ViewResult>(await controller.Deliveries(refillPage: int.MaxValue)).Model);
        Assert.Equal(before.RefillTotal + 2, after.RefillTotal);
        Assert.InRange(after.RefillPage, 1, Math.Max(1, (after.RefillTotal + 24) / 25));
        var clinic = await StaffAccounts.SignInAsync(factory, "redstar.demo@fixtures.test", "RedStarStaff");
        var html = await clinic.GetStringAsync("/Admin/RedStar?search=" + prefix + "&worklist=week");
        Assert.Contains("Needs verification", html);
        Assert.Contains("Replenishment due soon", html);
    }

    [Fact]
    public async Task Expired_report_uses_month_boundaries_and_preserves_month_in_pagination()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var controller = Controller(db, scope.ServiceProvider);
        var date = ClinicalClock.Today.AddMonths(-2);
        var start = new DateTime(date.Year, date.Month, 1);
        var month = start.ToString("yyyy-MM");
        var prefix = "MONTH-" + Guid.NewGuid().ToString("N") + "-";
        var before = Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(await controller.Index(search: prefix, worklist: "expired", month: month)).Model);
        for (var i = 1; i <= 28; i++)
            db.RedStarEnrollments.Add(new RedStarEnrollment
            {
                Patient = new RedStarPatient { PatientCode = prefix + i.ToString("D2"), FullName = "Synthetic monthly report patient" },
                Program = RedStarProgram.ART, Status = RedStarStatus.Deceased,
                StatusDate = i == 27 ? start.AddDays(-1) : i == 28 ? start.AddMonths(1) : start.AddDays(i - 1)
            });
        await db.SaveChangesAsync();
        var report = Assert.IsType<RedStarRegistryPage>(Assert.IsType<ViewResult>(await controller.Index(search: prefix, worklist: "expired", page: 2, month: month)).Model);
        Assert.Equal(26, report.Total);
        Assert.Equal(2, report.Page);
        Assert.Equal(before.Counts["expired"] + 26, report.Counts["expired"]);
        Assert.Equal(before.Counts["living"], report.Counts["living"]);
        Assert.Equal(prefix + "26", Assert.Single(report.Rows).PatientCode);
        var clinic = await StaffAccounts.SignInAsync(factory, "redstar.demo@fixtures.test", "RedStarStaff");
        var html = await clinic.GetStringAsync("/Admin/RedStar?search=" + prefix + "&worklist=expired&month=" + month);
        Assert.Contains("page=2", html);
        Assert.Contains("month=" + month, html);
        Assert.DoesNotContain(prefix + "27", html);
        Assert.DoesNotContain(prefix + "28", html);
        Assert.Contains("Expired " + start.ToString("dd MMM yyyy"), html);
    }
}
