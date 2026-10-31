using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class OutboxRecoveryTests(PortalFactory factory)
{
    private sealed class RecordingAccountSender : IEmailSender
    {
        public List<(string Recipient, string Subject, string Body)> Messages { get; } = [];
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Messages.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Account_payload_survives_restart_with_optional_plain_text_and_legacy_json(bool legacy)
    {
        var recipient = $"account-payload.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, recipient, false);
        using var accountScope = factory.Services.CreateScope();
        var userId = await accountScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users
            .Where(u => u.Email == recipient).Select(u => u.Id).SingleAsync();
        var sender = new RecordingAccountSender();
        WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
        }));
        Guid id;
        using (var first = Host())
        {
            const string html = "<p>SYNTHETIC-ACCOUNT-TOKEN</p>";
            var queue = first.Services.GetRequiredService<IAccountEmailQueue>();
            if (legacy) await queue.EnqueueAsync(userId, recipient, "Synthetic account subject", html);
            else await queue.EnqueueAsync(userId, recipient, "Synthetic account subject", html, "SYNTHETIC-PLAIN-TEXT");
            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var message = await db.AccountEmails.AsNoTracking().SingleAsync(m => m.UserId == userId);
            id = message.Id;
            var protector = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DRMC.AccountEmail.v1");
            Assert.DoesNotContain("SYNTHETIC", Encoding.UTF8.GetString(message.ProtectedPayload!));
            if (legacy)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Recipient = recipient, Subject = "Synthetic account subject", Body = html });
                Assert.DoesNotContain("PlainTextBody", Encoding.UTF8.GetString(bytes));
                await db.AccountEmails.Where(m => m.Id == id).ExecuteUpdateAsync(set => set.SetProperty(m => m.ProtectedPayload, protector.Protect(bytes)));
                Assert.Null(JsonSerializer.Deserialize<AccountEmailPayload>(bytes)!.PlainTextBody);
            }
            else
            {
                var payload = JsonSerializer.Deserialize<AccountEmailPayload>(protector.Unprotect(message.ProtectedPayload!))!;
                Assert.Equal("SYNTHETIC-PLAIN-TEXT", payload.PlainTextBody);
                Assert.Equal(html, payload.Body);
            }
            Assert.Empty(sender.Messages);
        }
        using var restarted = Host();
        for (var batch = 0; batch < 20 && !sender.Messages.Any(m => m.Recipient == recipient); batch++)
            await restarted.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
        Assert.Single(sender.Messages, m => m.Recipient == recipient && m.Subject == "Synthetic account subject" && m.Body == "<p>SYNTHETIC-ACCOUNT-TOKEN</p>");
        using var completedScope = restarted.Services.CreateScope();
        var completed = await completedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountEmails.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Equal(AccountEmailStatus.Delivered, completed.Status);
        Assert.Null(completed.ProtectedPayload);
    }

    private sealed class SlowBatchSender(TestClock clock, bool fail) : IEmailSender
    {
        public Channel<TaskCompletionSource> Attempts { get; } = Channel.CreateUnbounded<TaskCompletionSource>();
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            clock.Now = clock.Now.AddSeconds(30);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Attempts.Writer.WriteAsync(release);
            await release.Task;
            if (fail) throw new TimeoutException("Synthetic provider timeout.");
        }
    }

    private static void RefreshOtherWorkers(PortalWorkerHealth health)
    {
        foreach (var worker in new[] { "imports", "security-cleanup", "temporary-documents", "operations" }) health.Success(worker);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Active_mail_batches_remain_ready_and_stopped_workers_expire(bool accountEmail, bool fail)
    {
        // A separate database keeps other tests' due messages out of the controlled 25-message batch.
        var isolated = new PortalFactory();
        try
        {
            await isolated.InitializeAsync();
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var sender = new SlowBatchSender(clock, fail);
            var workerName = accountEmail ? "account-email" : "outbound-notifications";
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccountEmail:WorkerEnabled"] = accountEmail.ToString(),
                ["Notifications:Outbox:WorkerEnabled"] = (!accountEmail).ToString()
            }).Build();
            using var health = new PortalWorkerHealth(clock, config);
            using var host = isolated.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<IEmailSender>(); services.AddSingleton<IEmailSender>(sender);
                services.RemoveAll<PortalWorkerHealth>(); services.AddSingleton(health);
            }));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
            var accountDispatcher = host.Services.GetRequiredService<AccountEmailDispatcher>();
            var outboundDispatcher = host.Services.GetRequiredService<OutboundNotificationDispatcher>();
            clock.Now = clock.Now.AddMinutes(3);
            RefreshOtherWorkers(health);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            // Empty queue scans also prove that an idle worker is functioning.
            if (accountEmail) await accountDispatcher.DispatchOnceAsync(reportProgress: () => health.Success(workerName));
            else await outboundDispatcher.DispatchOnceAsync(reportProgress: () => health.Success(workerName));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

            var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToArray();
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = await db.Users.Where(u => u.Email == PortalFactory.PrimaryEmail).Select(u => u.Id).SingleAsync();
            foreach (var id in ids)
            {
                if (accountEmail) host.Services.GetRequiredService<IAccountEmailQueue>().Stage(db, id, userId,
                    "batch@fixtures.test", "Batch fixture", "Synthetic batch delivery.");
                else host.Services.GetRequiredService<IOutboundNotificationQueue>().Stage(db, id,
                    OutboundNotificationChannel.Email, new("batch@fixtures.test", "Batch fixture", "Synthetic batch delivery."));
            }
            await db.SaveChangesAsync();
            var started = clock.Now;
            using BackgroundService worker = accountEmail
                ? new AccountEmailWorker(accountDispatcher, Options.Create(new AccountEmailOptions()), clock, NullLogger<AccountEmailWorker>.Instance, health)
                : new OutboundNotificationWorker(outboundDispatcher, Options.Create(new OutboundNotificationOptions()), clock, NullLogger<OutboundNotificationWorker>.Instance, health);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await worker.StartAsync(default);
            try
            {
                for (var attempt = 0; attempt < ids.Length; attempt++)
                {
                    var release = await sender.Attempts.Reader.ReadAsync(deadline.Token);
                    try
                    {
                        RefreshOtherWorkers(health);
                        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", deadline.Token)).StatusCode);
                    }
                    finally { release.TrySetResult(); }
                }
                while (accountEmail
                    ? await db.AccountEmails.AnyAsync(m => ids.Contains(m.Id) && m.Status == AccountEmailStatus.Sending, deadline.Token)
                    : await db.OutboundNotifications.AnyAsync(m => ids.Contains(m.Id) && m.Status == OutboundNotificationStatus.Sending, deadline.Token))
                    await Task.Delay(10, deadline.Token);
            }
            finally { await worker.StopAsync(default); }
            Assert.Equal(TimeSpan.FromSeconds(25 * 30), clock.Now - started);
            if (accountEmail)
            {
                var messages = await db.AccountEmails.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync();
                Assert.Equal(25, messages.Count);
                Assert.All(messages, m => { Assert.Equal(1, m.Attempts); Assert.Equal(fail ? AccountEmailStatus.Pending : AccountEmailStatus.Delivered, m.Status); });
            }
            else
            {
                var messages = await db.OutboundNotifications.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync();
                Assert.Equal(25, messages.Count);
                Assert.All(messages, m => { Assert.Equal(1, m.AttemptCount); Assert.Equal(fail ? OutboundNotificationStatus.Pending : OutboundNotificationStatus.Delivered, m.Status); });
            }
            clock.Now = clock.Now.AddMinutes(2).AddSeconds(1);
            RefreshOtherWorkers(health);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally { await ((IAsyncLifetime)isolated).DisposeAsync(); }
    }

    private sealed class UnavailableQueueReads : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && (command.CommandText.Contains("[AccountEmails]") || command.CommandText.Contains("[OutboundNotifications]")))
                throw new InvalidOperationException("Synthetic queue database failure.");
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Queue_scan_failures_and_shutdown_do_not_report_progress(bool accountEmail)
    {
        var fault = new UnavailableQueueReads();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(fault))));
        var account = host.Services.GetRequiredService<AccountEmailDispatcher>();
        var outbound = host.Services.GetRequiredService<OutboundNotificationDispatcher>();
        var progress = 0;
        Task Dispatch(CancellationToken token) => accountEmail
            ? account.DispatchOnceAsync(token, () => progress++) : outbound.DispatchOnceAsync(token, () => progress++);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Dispatch(cancelled.Token));
        fault.Armed = true;
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => Dispatch(default)); }
        finally { fault.Armed = false; }
        Assert.Equal(0, progress);
    }

    private sealed class ExpiringSender(TestClock clock, string recipient) : IEmailSender
    {
        public int Attempts { get; private set; }
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (email != recipient) return Task.CompletedTask;
            Attempts++;
            clock.Now = clock.Now.AddSeconds(2);
            throw new IOException("Synthetic expired transport.");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Expiry_during_a_failed_send_takes_precedence_over_retry_and_attempt_exhaustion(int attempts)
    {
        var email = $"expiry.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        await factory.Services.GetRequiredService<IAccountEmailQueue>().EnqueueAsync(userId, email, "Expiry", "EXPIRED-SECRET");
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await db.AccountEmails.Where(m => m.UserId == userId).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.ExpiresUtc, clock.Now.AddSeconds(1)).SetProperty(m => m.Attempts, attempts));
        var sender = new ExpiringSender(clock, email);
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(sender);
        }));
        var dispatcher = new AccountEmailDispatcher(host.Services.GetRequiredService<IServiceScopeFactory>(),
            host.Services.GetRequiredService<IDataProtectionProvider>(), Options.Create(new AccountEmailOptions()),
            clock, NullLogger<AccountEmailDispatcher>.Instance);
        for (var batch = 0; batch < 20 && sender.Attempts == 0; batch++)
            await dispatcher.DispatchOnceAsync();
        Assert.Equal(1, sender.Attempts);
        var expired = await db.AccountEmails.AsNoTracking().SingleAsync(m => m.UserId == userId);
        Assert.Equal(AccountEmailStatus.Expired, expired.Status);
        Assert.Null(expired.ProtectedPayload);
        Assert.Null(expired.LeaseOwner);
        Assert.NotNull(expired.CompletedUtc);
    }

    [Fact]
    public async Task Final_account_attempt_interrupted_by_restart_is_failed_instead_of_expired()
    {
        var email = $"outbox.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        using var accountScope = factory.Services.CreateScope();
        var id = await accountScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users
            .Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        await factory.Services.GetRequiredService<IAccountEmailQueue>().EnqueueAsync(id, email, "Verification", "PRIVATE-OUTBOX-TOKEN");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.AccountEmails.Where(m => m.UserId == id).ExecuteUpdateAsync(set => set
            .SetProperty(m => m.Status, AccountEmailStatus.Sending).SetProperty(m => m.Attempts, 5)
            .SetProperty(m => m.LeaseOwner, "crashed-final-worker")
            .SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1)));

        await factory.Services.GetRequiredService<AccountEmailDispatcher>().CleanupAsync();

        var message = await db.AccountEmails.AsNoTracking().SingleAsync(m => m.UserId == id);
        Assert.Equal(AccountEmailStatus.Failed, message.Status);
        Assert.Null(message.ProtectedPayload);
        Assert.Null(message.LeaseOwner);
        Assert.NotNull(message.CompletedUtc);
    }
}
