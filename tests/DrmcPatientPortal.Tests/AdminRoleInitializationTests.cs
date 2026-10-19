using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

public sealed class AdminRoleInitializationTests
{
    [Fact]
    public async Task Concurrent_replicas_create_each_builtin_role_once_and_can_restart()
    {
        var server = Environment.GetEnvironmentVariable(PortalFactory.ConnectionVariable)
            ?? throw new InvalidOperationException("Configure the SQL Server test connection.");
        var connection = $"{server.TrimEnd(';')};Database=DrmcRoleTests_{Guid.NewGuid():N}";
        var barrier = new ConcurrentRoleInsert();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection).AddInterceptors(barrier));
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.MaxLengthForKeys = 128)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().Build();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

            await Task.WhenAll(AdminBootstrap.InitializeAsync(provider, configuration),
                AdminBootstrap.InitializeAsync(provider, configuration));
            Assert.Equal(2, barrier.Arrivals);
            await AdminBootstrap.InitializeAsync(provider, configuration);

            await using var verification = provider.CreateAsyncScope();
            var roles = await verification.ServiceProvider.GetRequiredService<ApplicationDbContext>().Roles
                .AsNoTracking().Select(r => r.Name).OrderBy(n => n).ToArrayAsync();
            Assert.Equal(StaffRoles.All.OrderBy(n => n), roles);
        }
        finally
        {
            await using var cleanup = provider.CreateAsyncScope();
            await cleanup.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private sealed class ConcurrentRoleInsert : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<IdentityRole>()
                .Any(e => e.State == EntityState.Added && e.Entity.Name == "Admin") != true) return result;
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            // Both independent contexts must reach INSERT before either can save.
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
