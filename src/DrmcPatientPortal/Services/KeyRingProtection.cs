using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrmcPatientPortal.Services;

public sealed class KeyRingCertificateOptions
{
    public string Path { get; set; } = string.Empty;
    public string? PasswordFile { get; set; }
}

public sealed class KeyRingProtectionOptions
{
    public bool ProtectKeysWithDpapi { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePasswordFile { get; set; }
    public KeyRingCertificateOptions[] DecryptionCertificates { get; set; } = [];
}

public static class KeyRingProtection
{
    public const string PatientDocumentPurpose = "DRMC.PatientDocuments.File.v2";
    public const string PatientStagingTokenPurpose = "DRMC.PatientDocuments.StagingToken.v1";
    public const string LabReportPurpose = "DRMC.LabReports.v1";
    public const string RadiologyReportPurpose = "DRMC.RadiologyReports.v1";
    public const string OutboundNotificationPurpose = "DRMC.OutboundNotification.v1";
    public const string RedStarPhiPurpose = "DrmcPatientPortal.RedStar.PhiProtection.v1";
    private static readonly XNamespace ProtectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";

    // Resolved by Verify so DI owns certificates for the encryptor's entire lifetime.
    private sealed class Certificates : IDisposable
    {
        public List<X509Certificate2> Items { get; } = [];
        public void Dispose() { foreach (var certificate in Items) certificate.Dispose(); }
    }

    public static Task ConfigureAsync(IDataProtectionBuilder builder, string path, bool protectKeysWithDpapi) =>
        ConfigureAsync(builder, path, new() { ProtectKeysWithDpapi = protectKeysWithDpapi }, false);

    public static async Task ConfigureAsync(IDataProtectionBuilder builder, string path, KeyRingProtectionOptions options,
        bool requireEncryption, string? contentRoot = null, CancellationToken token = default)
    {
        var useCertificate = !string.IsNullOrWhiteSpace(options.CertificatePath);
        if (useCertificate && options.ProtectKeysWithDpapi)
            throw new InvalidOperationException("Choose DataProtection:CertificatePath or ProtectKeysWithDpapi, not both.");
        if (!useCertificate && (options.CertificatePasswordFile is not null || options.DecryptionCertificates.Length != 0))
            throw new InvalidOperationException("DataProtection:CertificatePath is required for certificate protection settings.");
        if (requireEncryption && !useCertificate && !options.ProtectKeysWithDpapi)
            throw new InvalidOperationException("DataProtection requires encryption outside Development. Configure CertificatePath (portable) or Windows ProtectKeysWithDpapi.");
        if (options.ProtectKeysWithDpapi && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI requires the original Windows account and machine. Use certificate protection for containers.");

        var certificates = new Certificates();
        try
        {
            IXmlEncryptor? encryptor = null;
            IXmlDecryptor? targetDecryptor = null;
            ServiceProvider? certificateServices = null;
            try
            {
                if (useCertificate)
                {
                    var root = contentRoot ?? Directory.GetCurrentDirectory();
                    certificates.Items.Add(LoadCertificate(options.CertificatePath!, options.CertificatePasswordFile, root));
                    foreach (var previous in options.DecryptionCertificates)
                        certificates.Items.Add(LoadCertificate(previous.Path, previous.PasswordFile, root));
                    var services = new ServiceCollection();
                    services.AddLogging();
                    services.AddDataProtection().UnprotectKeysWithAnyCertificate(certificates.Items.ToArray());
                    certificateServices = services.BuildServiceProvider();
                    targetDecryptor = new EncryptedXmlDecryptor(certificateServices);
                    encryptor = new CertificateXmlEncryptor(certificates.Items[0], NullLoggerFactory.Instance);
                    builder.ProtectKeysWithCertificate(certificates.Items[0]).UnprotectKeysWithAnyCertificate(certificates.Items.ToArray());
                }
                else if (options.ProtectKeysWithDpapi)
                {
                    encryptor = new DpapiXmlEncryptor(false, NullLoggerFactory.Instance);
                    targetDecryptor = new DpapiXmlDecryptor();
                    ProtectNewKeys(builder);
                }

                if (encryptor is not null)
                {
                    var probe = new XElement("probe", Guid.NewGuid().ToString("N"));
                    var encrypted = encryptor.Encrypt(probe);
                    if (!XNode.DeepEquals(Canonical(targetDecryptor!.Decrypt(encrypted.EncryptedElement)), Canonical(probe)))
                        throw new CryptographicException("Data Protection private-key round-trip failed.");
                }
                await ConvertAsync(path, encryptor, targetDecryptor, useCertificate, token);
            }
            finally { certificateServices?.Dispose(); }
            builder.Services.AddSingleton(_ => certificates);
        }
        catch
        {
            certificates.Dispose();
            throw;
        }
    }

    private static X509Certificate2 LoadCertificate(string path, string? passwordFile, string root)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("A DataProtection certificate path is required.");
        var password = passwordFile is null ? null : File.ReadAllText(System.IO.Path.GetFullPath(passwordFile, root)).TrimEnd('\r', '\n');
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(System.IO.Path.GetFullPath(path, root), password, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey || certificate.GetRSAPrivateKey() is not { } rsa)
        {
            certificate.Dispose();
            throw new InvalidOperationException("DataProtection certificates must contain an RSA private key.");
        }
        rsa.Dispose();
        return certificate;
    }

    // Descriptors are lazy: prove every historical key is readable before database upgrades/traffic.
    public static void Verify(IServiceProvider services)
    {
        _ = services.GetRequiredService<Certificates>();
        foreach (var key in services.GetRequiredService<IKeyManager>().GetAllKeys())
            _ = key.CreateEncryptor() ?? throw new CryptographicException("A historical Data Protection key is unreadable.");
    }

    public static async Task ProtectAsync(string path, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires the original Windows account and machine.");
        await ConvertAsync(path, new DpapiXmlEncryptor(false, NullLoggerFactory.Instance), new DpapiXmlDecryptor(), false, token);
    }

    private static async Task ConvertAsync(string path, IXmlEncryptor? encryptor, IXmlDecryptor? targetDecryptor,
        bool certificateMode, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Test actual access under the runtime identity, also for an existing ring.
        await using (var probe = new FileStream(System.IO.Path.Combine(path, ".write-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            await probe.WriteAsync(new byte[] { 0 }, token);

        FileStream? gate = null;
        for (var attempt = 0; gate is null; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { gate = new FileStream(System.IO.Path.Combine(path, ".protection.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(100, token); }
        }
        await using (gate)
        {
            var replacements = new List<(string File, XDocument Xml)>();
            // Preflight every envelope before rewriting. Never instantiate a type supplied by XML.
            foreach (var file in Directory.EnumerateFiles(path, "key-*.xml"))
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 1024 * 1024 });
                var xml = XDocument.Load(reader);
                if (xml.Root?.Name != "key" || !Guid.TryParse((string?)xml.Root.Attribute("id"), out _))
                    throw new InvalidDataException("Invalid Data Protection key file.");
                var changed = false;
                foreach (var envelope in xml.Descendants(ProtectionNamespace + "encryptedSecret").ToArray())
                {
                    if (encryptor is null) throw new InvalidOperationException("The key ring contains protected keys. Keep key protection configured; plaintext downgrade is not supported.");
                    var type = ((string?)envelope.Attribute("decryptorType"))?.Split(',')[0];
                    var payload = envelope.Elements().Single();
                    XElement secret;
                    if (type == typeof(DpapiXmlDecryptor).FullName)
                    {
                        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Convert the DPAPI ring to certificate protection under its original Windows identity before moving it to containers.");
                        secret = new DpapiXmlDecryptor().Decrypt(payload);
                        if (!certificateMode) continue;
                    }
                    else if (certificateMode && type == typeof(EncryptedXmlDecryptor).FullName)
                    {
                        _ = targetDecryptor!.Decrypt(payload);
                        continue; // Keep historical certificate envelopes.
                    }
                    else throw new InvalidOperationException("The key ring uses incompatible key protection. Restore the original protection configuration.");
                    envelope.ReplaceWith(Wrap(secret, encryptor, targetDecryptor!));
                    changed = true;
                }
                var plaintext = xml.Descendants().Where(element => (string?)element.Attribute(ProtectionNamespace + "requiresEncryption") == "true" &&
                    !element.Ancestors(ProtectionNamespace + "encryptedSecret").Any()).ToArray();
                foreach (var secret in plaintext)
                {
                    if (encryptor is null) continue; // Development only.
                    secret.ReplaceWith(Wrap(secret, encryptor, targetDecryptor!));
                    changed = true;
                }
                if (changed) replacements.Add((file, xml));
            }
            foreach (var (file, xml) in replacements)
            {
                var temporary = file + ".protected-" + Guid.NewGuid().ToString("N");
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await xml.SaveAsync(output, SaveOptions.DisableFormatting, token);
                        output.Flush(true);
                    }
                    File.Move(temporary, file, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }

    private static XElement Wrap(XElement secret, IXmlEncryptor encryptor, IXmlDecryptor decryptor)
    {
        var encrypted = encryptor.Encrypt(new XElement(secret));
        if (!XNode.DeepEquals(Canonical(decryptor.Decrypt(encrypted.EncryptedElement)), Canonical(secret)))
            throw new CryptographicException("Key protection round-trip failed; the original key was not replaced.");
        return new XElement(ProtectionNamespace + "encryptedSecret",
            new XAttribute("decryptorType", encrypted.DecryptorType.AssemblyQualifiedName!), encrypted.EncryptedElement);
    }

    private static XElement Canonical(XElement element)
    {
        var copy = new XElement(element);
        foreach (var attribute in copy.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToArray()) attribute.Remove();
        return copy;
    }

    public static IDataProtectionBuilder ProtectNewKeys(IDataProtectionBuilder builder)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires the original Windows account and machine.");
        return builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
    }
}
