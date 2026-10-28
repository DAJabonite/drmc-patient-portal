using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

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
    private readonly KeyRingProtectionOptions _protection;

    public PortalFactory()
    {
        var server = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException(
                $"Set {ConnectionVariable} to a SQL Server connection string without a database name, " +
                "for example: Server=localhost,1433;User Id=sa;Password=...;Encrypt=True;TrustServerCertificate=True");
        _connectionString = $"{server.TrimEnd(';')};Database=DrmcPortalTests_{Guid.NewGuid():N}";
        _protection = TestCertificates.Create(_storageRoot, "portal");
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
        builder.UseSetting("DataProtection:CertificatePath", _protection.CertificatePath);
        builder.UseSetting("DataProtection:CertificatePasswordFile", _protection.CertificatePasswordFile);
        builder.UseSetting("DataProtection:ProtectKeysWithDpapi", "false");
        builder.ConfigureTestServices(services =>
        {
            // Fixed-clock tests drive maintenance explicitly; background cleanup would otherwise
            // delete another host's fixtures when a test advances its clock into the future.
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(PortalSecurityCleanupWorker)).ToArray()) services.Remove(descriptor);
        });
        builder.UseSetting("PatientDocuments:RootPath", Path.Combine(_storageRoot, "documents"));
        builder.UseSetting("PatientDocuments:TemporaryPath", Path.Combine(_storageRoot, "uploads"));
        builder.UseSetting("LabReports:RootPath", Path.Combine(_storageRoot, "reports"));
        builder.UseSetting("RadiologyReports:RootPath", Path.Combine(_storageRoot, "radiology-reports"));
        builder.UseSetting("ResultStaging:Enabled", "false");
        builder.UseSetting("ResultStaging:LaboratoryPath", Path.Combine(_storageRoot, "result-staging", "Laboratory"));
        builder.UseSetting("ResultStaging:RadiologyPath", Path.Combine(_storageRoot, "result-staging", "Radiology"));
        builder.UseSetting("ResultStaging:QuarantinePath", Path.Combine(_storageRoot, "result-staging", "Quarantine"));
        builder.UseSetting("AdvisoryImages:RootPath", Path.Combine(_storageRoot, "advisory-images"));
        builder.UseSetting("Imports:StagingPath", Path.Combine(_storageRoot, "imports"));
        builder.UseSetting("AccountEmail:WorkerEnabled", "false");
        builder.UseSetting("Notifications:Outbox:WorkerEnabled", "false");
        builder.UseSetting("RedStar:WorkerEnabled", "false");
        builder.UseSetting("RedStar:RemindersEnabled", "false");
        builder.UseSetting("WebsiteUsage:Enabled", "false");
        builder.UseSetting("PortalOperations:MonitorWorkers", "false");
        foreach (var setting in new[] { "AuthenticationPerIp", "AuthenticationPerAccount", "EmailPerIp", "EmailPerAccount", "ScansPerIp", "PdfPerUser" })
            builder.UseSetting("RateLimits:" + setting, "100000");
    }

    public HttpClient CreatePortalClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    // Identity store options shape the EF model, so resolve the context with the same Identity
    // configuration the app uses; a bare `new ApplicationDbContext(...)` builds a different model.
    private async Task<ServiceProvider> DatabaseServicesAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await KeyRingProtection.ConfigureAsync(services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_storageRoot, "keys"))).SetApplicationName("DRMC.PatientPortal"),
            Path.Combine(_storageRoot, "keys"), _protection, true);
        services.AddSingleton(sp => new RedStarPhiProtection(sp.GetRequiredService<IDataProtectionProvider>(), Path.Combine(_storageRoot, "keys")));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_connectionString));
        // AddDefaultIdentity (used by the app) caps Identity key columns at 128 characters.
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.MaxLengthForKeys = 128).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        var provider = services.BuildServiceProvider();
        KeyRingProtection.Verify(provider);
        return provider;
    }

    public async Task InitializeAsync()
    {
        // Fixtures require an already migrated schema, so migrate before the host starts.
        await using (var provider = await DatabaseServicesAsync())
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await RedStarProtectionUpgrade.RunAsync(db);
            var environment = new TestEnvironment { ContentRootPath = _storageRoot, WebRootPath = Path.Combine(_storageRoot, "wwwroot") };
            var storage = new PatientDocumentStorage(environment, Options.Create(new PatientDocumentStorageOptions
            {
                RootPath = Path.Combine(_storageRoot, "documents"), TemporaryPath = Path.Combine(_storageRoot, "uploads")
            }), provider.GetRequiredService<IDataProtectionProvider>(), null!);
            await PatientDocumentProtectionUpgrade.RunAsync(db, storage, environment);
        }
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await using (var provider = await DatabaseServicesAsync())
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }
}

internal static class TestCertificates
{
    public static KeyRingProtectionOptions Create(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=DRMC synthetic " + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var path = Path.Combine(directory, name + ".pfx");
        var passwordFile = Path.Combine(directory, name + ".password");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        File.WriteAllText(passwordFile, password + "\n");
        return new() { CertificatePath = path, CertificatePasswordFile = passwordFile };
    }
}

[CollectionDefinition(Name)]
public sealed class PortalCollection : ICollectionFixture<PortalFactory>
{
    public const string Name = "Portal";
}
