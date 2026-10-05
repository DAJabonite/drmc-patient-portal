using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

// Boots the real app against a throwaway SQL Server database that is migrated, seeded with
// development fixtures, and dropped after the test run.
public sealed class PortalFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionVariable = "DRMC_TEST_SQLSERVER";
    public const string PrimaryEmail = "primary.patient@fixtures.test";
    public const string EmptyEmail = "empty.patient@fixtures.test";
    public const string Password = "Fixture#Pass2026";

    private readonly string _connectionString;
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "drmc-tests-" + Guid.NewGuid().ToString("N"));

    public PortalFactory()
    {
        var server = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException(
                $"Set {ConnectionVariable} to a SQL Server connection string without a database name, " +
                "for example: Server=localhost,1433;User Id=sa;Password=...;Encrypt=True;TrustServerCertificate=True");
        _connectionString = $"{server.TrimEnd(';')};Database=DrmcPortalTests_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("DevelopmentFixtures:Enabled", "true");
        builder.UseSetting("DevelopmentFixtures:Primary:Email", PrimaryEmail);
        builder.UseSetting("DevelopmentFixtures:Primary:Password", Password);
        builder.UseSetting("DevelopmentFixtures:Empty:Email", EmptyEmail);
        builder.UseSetting("DevelopmentFixtures:Empty:Password", Password);
        builder.UseSetting("DataProtection:KeyRingPath", Path.Combine(_storageRoot, "keys"));
        builder.UseSetting("PatientDocuments:RootPath", Path.Combine(_storageRoot, "documents"));
        builder.UseSetting("PatientDocuments:TemporaryPath", Path.Combine(_storageRoot, "uploads"));
    }

    public HttpClient CreatePortalClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    // Identity store options shape the EF model, so resolve the context with the same Identity
    // configuration the app uses; a bare `new ApplicationDbContext(...)` builds a different model.
    private ServiceProvider DatabaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString));
        // AddDefaultIdentity (used by the app) caps Identity key columns at 128 characters.
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // Fixtures require an already migrated schema, so migrate before the host starts.
        await using (var provider = DatabaseServices())
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        _ = Server;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await using (var provider = DatabaseServices())
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }
}

[CollectionDefinition(Name)]
public sealed class PortalCollection : ICollectionFixture<PortalFactory>
{
    public const string Name = "Portal";
}
