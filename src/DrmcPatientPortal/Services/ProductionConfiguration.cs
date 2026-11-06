using System.Net;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DrmcPatientPortal.Data;

namespace DrmcPatientPortal.Services;

public static class ProductionConfiguration
{
    // One inventory for startup validation and ongoing storage readiness checks.
    public static Dictionary<string, string> StoragePaths(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var paths = new Dictionary<string, string>
        {
            ["DataProtection:KeyRingPath"] = "App_Data/DataProtectionKeys",
            ["PatientDocuments:RootPath"] = "App_Data/PatientIdDocuments",
            ["PatientDocuments:TemporaryPath"] = "App_Data/TempUploads",
            ["LabReports:RootPath"] = "App_Data/LabReports",
            ["RadiologyReports:RootPath"] = "App_Data/RadiologyReports",
            ["RedStarLabReports:RootPath"] = "App_Data/RedStarLabReports",
            ["AdvisoryImages:RootPath"] = "App_Data/AdvisoryImages",
            ["Imports:StagingPath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMC", "ImportStaging")
        };
        if (configuration.GetValue<bool>("ResultStaging:Enabled"))
        {
            paths["ResultStaging:LaboratoryPath"] = "App_Data/ResultStaging/Laboratory";
            paths["ResultStaging:RadiologyPath"] = "App_Data/ResultStaging/Radiology";
            paths["ResultStaging:QuarantinePath"] = "App_Data/ResultStaging/Quarantine";
        }
        foreach (var key in paths.Keys.ToArray())
            paths[key] = configuration[key] ?? paths[key];
        return paths;
    }

    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var failures = new List<string>();
        Add(new PortalOptionsValidator(environment, configuration).Validate(Options.DefaultName,
            Bind<PortalOptions>("Portal")));
        if (environment.IsDevelopment())
        {
            ThrowIfInvalid();
            return;
        }

        try
        {
            var sql = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection") ?? "");
            if (string.IsNullOrWhiteSpace(sql.DataSource) || string.IsNullOrWhiteSpace(sql.InitialCatalog) ||
                sql.DataSource.Contains("CONFIGURE_VIA_ENVIRONMENT", StringComparison.OrdinalIgnoreCase) ||
                sql.InitialCatalog.Contains("CONFIGURE_VIA_ENVIRONMENT", StringComparison.OrdinalIgnoreCase))
                failures.Add("ConnectionStrings:DefaultConnection requires a configured SQL Server and database.");
        }
        catch (ArgumentException)
        { failures.Add("ConnectionStrings:DefaultConnection must be a valid SQL Server connection string."); }

        Add(new EmailDeliveryOptionsValidator(environment).Validate(Options.DefaultName, Bind<EmailDeliveryOptions>(EmailDeliveryOptions.SectionName)));
        var sms = Bind<SmsDeliveryOptions>(SmsDeliveryOptions.SectionName);
        if (sms.Enabled && (!Uri.TryCreate(sms.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) || string.IsNullOrWhiteSpace(sms.ApiKey)))
            failures.Add("Notifications:Sms requires an HTTPS Endpoint without credentials and an ApiKey when enabled.");
        if (!ReadBoolean("Identity:RequireConfirmedAccount", true))
            failures.Add("Identity:RequireConfirmedAccount must be true outside Development.");
        if (!ReadBoolean("PortalOperations:MonitorWorkers", true))
            failures.Add("PortalOperations:MonitorWorkers must be true outside Development.");
        if (ReadBoolean("DevelopmentFixtures:Enabled"))
            failures.Add("DevelopmentFixtures:Enabled is allowed only in Development.");
        if (ReadBoolean("AdminBootstrap:Enabled") && (!MailAddress.TryCreate(configuration["AdminBootstrap:Email"], out var bootstrapEmail) ||
            !string.Equals(configuration["AdminBootstrap:Email"]?.Trim(), bootstrapEmail.Address, StringComparison.OrdinalIgnoreCase)))
            failures.Add("AdminBootstrap:Email requires a valid bare email address when bootstrap is enabled.");
        foreach (var proxy in configuration.GetSection("Proxy:KnownProxies").GetChildren())
            if (!IPAddress.TryParse(proxy.Value, out _)) failures.Add("Proxy:KnownProxies must contain explicit IP addresses.");

        // Bind and check the enabled flag before constructing the inventory, so malformed
        // values produce configuration names rather than raw binder exceptions.
        var staging = Bind<ResultStagingOptions>(ResultStagingOptions.SectionName);
        var paths = new Dictionary<string, string>();
        if (bool.TryParse(configuration["ResultStaging:Enabled"] ?? "false", out _))
        {
            foreach (var entry in StoragePaths(configuration, environment))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(configuration[entry.Key]) || !Path.IsPathFullyQualified(entry.Value))
                        throw new InvalidOperationException();
                    var root = new PrivateStorageRoot(environment, entry.Value).Path;
                    if (PrivateStorageRoot.PathsOverlap(environment.ContentRootPath, root) ||
                        root.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException();
                    for (string? ancestor = root; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                        if (File.Exists(ancestor)) throw new InvalidOperationException();
                    paths[entry.Key] = root;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
                { failures.Add($"{entry.Key} requires an accessible absolute directory outside the published site, without links."); }
            }
            var entries = paths.ToArray();
            for (var i = 0; i < entries.Length; i++)
                for (var j = i + 1; j < entries.Length; j++)
                    if (PrivateStorageRoot.PathsOverlap(entries[i].Value, entries[j].Value))
                        failures.Add($"{entries[i].Key} and {entries[j].Key} must use separate directories.");
        }
        Add(new ResultStagingOptionsValidator(environment, configuration).Validate(Options.DefaultName, staging));

        var protection = Bind<KeyRingProtectionOptions>("DataProtection");
        var certificate = !string.IsNullOrWhiteSpace(protection.CertificatePath);
        if (certificate == protection.ProtectKeysWithDpapi)
            failures.Add("DataProtection requires exactly one of CertificatePath or ProtectKeysWithDpapi.");
        if (protection.ProtectKeysWithDpapi && !OperatingSystem.IsWindows())
            failures.Add("DataProtection:ProtectKeysWithDpapi requires Windows; configure certificate protection on other platforms.");
        if (!certificate && (protection.CertificatePasswordFile is not null || protection.DecryptionCertificates.Length != 0))
            failures.Add("DataProtection:CertificatePath is required for certificate password and decryption-certificate settings.");
        if (certificate) ValidateSecretFile("DataProtection:CertificatePath", protection.CertificatePath);
        if (protection.CertificatePasswordFile is not null)
            ValidateSecretFile("DataProtection:CertificatePasswordFile", protection.CertificatePasswordFile);
        for (var i = 0; i < protection.DecryptionCertificates.Length; i++)
        {
            ValidateSecretFile($"DataProtection:DecryptionCertificates:{i}:Path", protection.DecryptionCertificates[i].Path);
            if (protection.DecryptionCertificates[i].PasswordFile is { } passwordFile)
                ValidateSecretFile($"DataProtection:DecryptionCertificates:{i}:PasswordFile", passwordFile);
        }
        ThrowIfInvalid();

        T Bind<T>(string section) where T : new()
        {
            try { return configuration.GetSection(section).Get<T>() ?? new(); }
            catch (InvalidOperationException) { failures.Add($"{section} contains an invalid configuration value."); return new(); }
        }
        bool ReadBoolean(string key, bool fallback = false)
        {
            if (configuration[key] is not { } value) return fallback;
            if (bool.TryParse(value, out var enabled)) return enabled;
            failures.Add($"{key} must be true or false.");
            return fallback;
        }
        void Add(ValidateOptionsResult result) { if (result.Failed) failures.AddRange(result.Failures); }
        void ThrowIfInvalid()
        {
            if (failures.Count > 0) throw new OptionsValidationException(Options.DefaultName, typeof(ProductionConfiguration), failures);
        }
        void ValidateSecretFile(string key, string? file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file)) throw new InvalidOperationException();
                var full = Path.GetFullPath(file);
                if (PrivateStorageRoot.PathsOverlap(environment.ContentRootPath, full) || paths.Values.Any(root => PrivateStorageRoot.PathsOverlap(root, full)))
                    throw new InvalidOperationException();
                // Validate ancestors without opening key material or changing ACLs.
                _ = new PrivateStorageRoot(environment, full);
                using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
            { failures.Add($"{key} requires a readable absolute file outside the published site and mutable storage, without links."); }
        }
    }

    public static async Task VerifySchemaAsync(ApplicationDbContext db, CancellationToken token = default)
    {
        const string preparation = "Database schema is not ready. Stop all portal processes and run: dotnet DrmcPatientPortal.dll redstar-protection upgrade.";
        try
        {
            var expected = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var applied = (await db.Database.GetAppliedMigrationsAsync(token)).ToHashSet(StringComparer.Ordinal);
            if (!expected.SetEquals(applied)) throw new InvalidOperationException(preparation);
        }
        catch (SqlException)
        { throw new InvalidOperationException("SQL Server is unavailable or its schema cannot be read. Check ConnectionStrings:DefaultConnection and database permissions. " + preparation); }
    }
}
