using System.Collections.Concurrent;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class OutboundNotificationTests(PortalFactory factory)
{
    private sealed class Transport : IEmailSender, ISmsSender
    {
        public bool Fail { get; set; }
        public Func<string, Task>? BeforeSend { get; set; }
        public ConcurrentQueue<(string Recipient, string Body)> Sent { get; } = new();
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Send(email, htmlMessage);
        public Task SendSmsAsync(string number, string message, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Send(number, message);
        }
        private async Task Send(string recipient, string body)
        {
            if (Fail) throw new IOException("PRIVATE-PROVIDER-TOKEN-AND-RECIPIENT");
            if (BeforeSend is not null) await BeforeSend(recipient);
            Sent.Enqueue((recipient, body));
        }
    }

    private WebApplicationFactory<Program> Host(Transport transport, TestClock clock, int maximumAttempts = 5) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.RemoveAll<ISmsSender>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<IEmailSender>(transport);
            services.AddSingleton<ISmsSender>(transport);
            services.AddSingleton<TimeProvider>(clock);
            services.Configure<OutboundNotificationOptions>(o => o.MaximumAttempts = maximumAttempts);
        }));

    private async Task<OutboundNotificationMessage> Message(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundNotifications.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Hosted_worker_picks_up_persisted_work_at_startup_and_new_work_while_running()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var sender = new Transport();
        var recipient = $"hosted.{Guid.NewGuid():N}@fixtures.test";
        Guid persistedId;
        using (var stopped = Host(sender, clock))
            persistedId = await stopped.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(
                OutboundNotificationChannel.Sms, new(recipient, null, "BEFORE-START"));
        using var configured = Host(sender, clock);
        using var running = configured.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<OutboundNotificationOptions>(o => o.WorkerEnabled = true)));
        var queue = running.Services.GetRequiredService<IOutboundNotificationQueue>();
        await WaitForDelivery(persistedId);
        var nextId = await queue.EnqueueAsync(OutboundNotificationChannel.Email, new(recipient, "Periodic", "WHILE-RUNNING"));
        await WaitForDelivery(nextId);
        Assert.Single(sender.Sent, m => m.Recipient == recipient && m.Body == "BEFORE-START");
        Assert.Single(sender.Sent, m => m.Recipient == recipient && m.Body == "WHILE-RUNNING");
    }

    private async Task WaitForDelivery(Guid id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await Message(id)).Status == OutboundNotificationStatus.Delivered) return;
            await Task.Delay(100);
        }
        Assert.Equal(OutboundNotificationStatus.Delivered, (await Message(id)).Status);
    }

    [Theory]
    [InlineData(OutboundNotificationChannel.Email)]
    [InlineData(OutboundNotificationChannel.Sms)]
    public async Task Pending_encrypted_message_survives_a_fresh_host_and_key_provider(OutboundNotificationChannel channel)
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var recipient = $"private.{Guid.NewGuid():N}@fixtures.test";
        var sender = new Transport();
        Guid id;
        using (var first = Host(sender, clock))
        {
            id = await first.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(channel,
                new(recipient, "PRIVATE-SUBJECT", "PRIVATE-BODY-TOKEN"));
            var queued = await Message(id);
            Assert.Equal(OutboundNotificationStatus.Pending, queued.Status);
            Assert.Equal(0, queued.AttemptCount);
            var bytes = Encoding.UTF8.GetString(queued.PayloadProtected!);
            Assert.DoesNotContain(recipient, bytes);
            Assert.DoesNotContain("PRIVATE-SUBJECT", bytes);
            Assert.DoesNotContain("PRIVATE-BODY-TOKEN", bytes);
            Assert.Empty(sender.Sent);
        }
        using var restarted = Host(sender, clock);
        await restarted.Services.GetRequiredService<OutboundNotificationDispatcher>().DispatchOnceAsync();
        Assert.Single(sender.Sent, m => m.Recipient == recipient && m.Body == "PRIVATE-BODY-TOKEN");
        var delivered = await Message(id);
        Assert.Equal(OutboundNotificationStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.AttemptCount);
        Assert.Null(delivered.PayloadProtected);
        Assert.Null(delivered.LeaseOwner);
        Assert.NotEmpty(delivered.RowVersion);
    }

    [Theory]
    [InlineData(OutboundNotificationChannel.Email)]
    [InlineData(OutboundNotificationChannel.Sms)]
    public async Task Transient_failure_is_durable_and_retries_only_after_its_due_time(OutboundNotificationChannel channel)
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var sender = new Transport { Fail = true };
        var recipient = $"retry.{Guid.NewGuid():N}@fixtures.test";
        Guid id;
        using (var first = Host(sender, clock))
        {
            id = await first.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(channel, new(recipient, "Retry", "PRIVATE-RETRY"));
            await first.Services.GetRequiredService<OutboundNotificationDispatcher>().DispatchOnceAsync();
        }
        var pending = await Message(id);
        Assert.Equal(OutboundNotificationStatus.Pending, pending.Status);
        Assert.Equal(1, pending.AttemptCount);
        Assert.Equal(clock.Now.AddSeconds(30), pending.NextAttemptAtUtc);
        Assert.Equal(nameof(IOException), pending.LastError);
        Assert.NotNull(pending.PayloadProtected);
        sender.Fail = false;
        using var restarted = Host(sender, clock);
        var dispatcher = restarted.Services.GetRequiredService<OutboundNotificationDispatcher>();
        await dispatcher.DispatchOnceAsync();
        Assert.DoesNotContain(sender.Sent, m => m.Recipient == recipient);
        clock.Now = pending.NextAttemptAtUtc;
        await dispatcher.DispatchOnceAsync();
        Assert.Single(sender.Sent, m => m.Recipient == recipient);
        Assert.Equal(2, (await Message(id)).AttemptCount);
        Assert.Equal(OutboundNotificationStatus.Delivered, (await Message(id)).Status);
    }

    [Fact]
    public async Task Abandoned_lease_is_recovered_by_only_one_of_two_workers()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var sender = new Transport();
        var recipient = $"leased.{Guid.NewGuid():N}@fixtures.test";
        using var first = Host(sender, clock);
        var id = await first.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(OutboundNotificationChannel.Sms, new(recipient, null, "LEASED"));
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundNotifications.Where(m => m.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Status, OutboundNotificationStatus.Sending)
                    .SetProperty(m => m.AttemptCount, 1).SetProperty(m => m.LeaseOwner, "crashed-worker")
                    .SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)clock.Now.AddMinutes(2)));
        var a = first.Services.GetRequiredService<OutboundNotificationDispatcher>();
        await a.DispatchOnceAsync();
        Assert.DoesNotContain(sender.Sent, m => m.Recipient == recipient);
        clock.Now = clock.Now.AddMinutes(3);
        using var second = Host(sender, clock);
        await Task.WhenAll(a.DispatchOnceAsync(), second.Services.GetRequiredService<OutboundNotificationDispatcher>().DispatchOnceAsync());
        Assert.Single(sender.Sent, m => m.Recipient == recipient);
        Assert.Equal(2, (await Message(id)).AttemptCount);
    }

    [Fact]
    public async Task Exhausted_messages_are_dead_lettered_and_terminal_metadata_is_retained_for_seven_days()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var sender = new Transport { Fail = true };
        using var host = Host(sender, clock, maximumAttempts: 2);
        var id = await host.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(OutboundNotificationChannel.Email,
            new("failed@fixtures.test", "Failure", "PRIVATE-FAILED-TOKEN"));
        var dispatcher = host.Services.GetRequiredService<OutboundNotificationDispatcher>();
        await dispatcher.DispatchOnceAsync();
        clock.Now = (await Message(id)).NextAttemptAtUtc;
        await dispatcher.DispatchOnceAsync();
        var failed = await Message(id);
        Assert.Equal(OutboundNotificationStatus.DeadLetter, failed.Status);
        Assert.Equal(2, failed.AttemptCount);
        Assert.Equal(nameof(IOException), failed.LastError);
        Assert.Null(failed.PayloadProtected);
        Assert.Null(failed.LeaseOwner);
        Assert.NotNull(failed.CompletedAtUtc);
        await dispatcher.DispatchOnceAsync();
        Assert.Equal(2, (await Message(id)).AttemptCount);
        await dispatcher.CleanupAsync();
        Assert.Equal(id, (await Message(id)).Id);
        clock.Now = clock.Now.AddDays(8);
        await dispatcher.CleanupAsync();
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundNotifications.AnyAsync(m => m.Id == id));
    }

    [Fact]
    public async Task Expiry_and_interrupted_final_attempt_clear_payload_without_another_send()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var sender = new Transport();
        using var host = Host(sender, clock, maximumAttempts: 2);
        var queue = host.Services.GetRequiredService<IOutboundNotificationQueue>();
        var expiredId = await queue.EnqueueAsync(OutboundNotificationChannel.Sms, new("expired@fixtures.test", null, "EXPIRED"));
        var finalId = await queue.EnqueueAsync(OutboundNotificationChannel.Email, new("final@fixtures.test", "Final", "FINAL"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.OutboundNotifications.Where(m => m.Id == expiredId).ExecuteUpdateAsync(set => set.SetProperty(m => m.ExpiresAtUtc, clock.Now.AddSeconds(-1)));
            await db.OutboundNotifications.Where(m => m.Id == finalId).ExecuteUpdateAsync(set => set
                .SetProperty(m => m.Status, OutboundNotificationStatus.Sending).SetProperty(m => m.AttemptCount, 2)
                .SetProperty(m => m.LeaseOwner, "final-crash").SetProperty(m => m.LeaseExpiresAtUtc, (DateTimeOffset?)clock.Now.AddSeconds(-1)));
        }
        await host.Services.GetRequiredService<OutboundNotificationDispatcher>().DispatchOnceAsync();
        Assert.Equal(OutboundNotificationStatus.Expired, (await Message(expiredId)).Status);
        Assert.Equal(OutboundNotificationStatus.DeadLetter, (await Message(finalId)).Status);
        Assert.Null((await Message(expiredId)).PayloadProtected);
        Assert.Null((await Message(finalId)).PayloadProtected);
        Assert.DoesNotContain(sender.Sent, m => m.Recipient is "expired@fixtures.test" or "final@fixtures.test");
    }

    private sealed class LostInsertAcknowledgement : SaveChangesInterceptor
    {
        public int Failures { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            if (Failures == 0 && eventData.Context!.ChangeTracker.Entries<OutboundNotificationMessage>().Any())
            {
                Failures++;
                throw new TimeoutException("Synthetic insert committed before acknowledgement.");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Standalone_enqueue_recovers_a_lost_commit_acknowledgement_without_duplicate_rows()
    {
        var fault = new LostInsertAcknowledgement();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(fault))));
        var id = await host.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(OutboundNotificationChannel.Sms,
            new("synthetic@fixtures.test", null, "IDEMPOTENT-ENVELOPE"));
        Assert.Equal(1, fault.Failures);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundNotifications.CountAsync(m => m.Id == id));
    }

    [Fact]
    public async Task Old_attempt_cannot_complete_a_replacement_lease_on_the_same_dispatcher()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempt = 0;
        var recipient = $"fenced.{Guid.NewGuid():N}@fixtures.test";
        var sender = new Transport { BeforeSend = address =>
        {
            if (address != recipient) return Task.CompletedTask;
            if (Interlocked.Increment(ref attempt) == 1) { firstStarted.SetResult(); return releaseFirst.Task; }
            secondStarted.SetResult(); return releaseSecond.Task;
        } };
        using var host = Host(sender, clock);
        var id = await host.Services.GetRequiredService<IOutboundNotificationQueue>().EnqueueAsync(OutboundNotificationChannel.Email, new(recipient, "Fenced", "FENCED"));
        var dispatcher = host.Services.GetRequiredService<OutboundNotificationDispatcher>();
        var first = dispatcher.DispatchOnceAsync();
        Task second = Task.CompletedTask;
        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Now = clock.Now.AddMinutes(3);
            second = dispatcher.DispatchOnceAsync();
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            releaseFirst.SetResult();
            await first;
            var claimed = await Message(id);
            Assert.Equal(OutboundNotificationStatus.Sending, claimed.Status);
            Assert.Equal(2, claimed.AttemptCount);
            Assert.NotNull(claimed.PayloadProtected);
            releaseSecond.SetResult();
            await second;
            Assert.Equal(OutboundNotificationStatus.Delivered, (await Message(id)).Status);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }
}
