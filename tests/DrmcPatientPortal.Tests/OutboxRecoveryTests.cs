using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class OutboxRecoveryTests(PortalFactory factory)
{
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
