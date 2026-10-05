using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrmcPatientPortal.Services;

public static class KeyRingProtection
{
    public static async Task ConfigureAsync(IDataProtectionBuilder builder, string path, bool protectKeysWithDpapi)
    {
        if (protectKeysWithDpapi)
        {
            await ProtectAsync(path);
            ProtectNewKeys(builder);
            return;
        }

        // Refuse a downgrade that would generate plaintext keys alongside protected keys.
        if (!Directory.Exists(path)) return;
        XNamespace ns = "http://schemas.asp.net/2015/03/dataProtection";
        foreach (var file in Directory.EnumerateFiles(path, "key-*.xml"))
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            if (XDocument.Load(reader).Descendants(ns + "encryptedSecret").Any())
                throw new InvalidOperationException("The key ring contains protected keys. Keep DataProtection:ProtectKeysWithDpapi enabled; see the README Key Protection and Recovery section before changing protection.");
        }
    }

    public static async Task ProtectAsync(string path, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This deployment requires Windows current-user DPAPI key protection.");
        Directory.CreateDirectory(path);
        // Serialize conversion across overlapped processes; never make plaintext backups.
        FileStream? gate = null;
        for (var attempt = 0; gate is null; attempt++)
        {
            try { gate = new FileStream(Path.Combine(path, ".protection.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(100, token); }
        }
        await using (gate)
        {
            var encryptor = new DpapiXmlEncryptor(false, NullLoggerFactory.Instance);
            var decryptor = new DpapiXmlDecryptor();
            XNamespace ns = "http://schemas.asp.net/2015/03/dataProtection";
            foreach (var file in Directory.EnumerateFiles(path, "key-*.xml"))
            {
                XDocument xml;
                using (var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 1024 * 1024 }))
                    xml = XDocument.Load(reader);
                var secrets = xml.Descendants().Where(element => (string?)element.Attribute(ns + "requiresEncryption") == "true" && !element.Ancestors(ns + "encryptedSecret").Any()).ToArray();
                foreach (var secret in secrets)
                {
                    var encrypted = encryptor.Encrypt(new XElement(secret));
                    if (!XNode.DeepEquals(Canonical(decryptor.Decrypt(encrypted.EncryptedElement)), Canonical(secret)))
                        throw new InvalidOperationException("Key protection round-trip failed; the original key was not replaced.");
                    secret.ReplaceWith(new XElement(ns + "encryptedSecret", new XAttribute("decryptorType", encrypted.DecryptorType.AssemblyQualifiedName!), encrypted.EncryptedElement));
                }
                if (secrets.Length == 0) continue;
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

    private static XElement Canonical(XElement element)
    {
        var copy = new XElement(element);
        foreach (var attribute in copy.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToArray()) attribute.Remove();
        return copy;
    }

    public static IDataProtectionBuilder ProtectNewKeys(IDataProtectionBuilder builder)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This deployment requires Windows current-user DPAPI key protection.");
        return builder.ProtectKeysWithDpapi(protectToLocalMachine: false);
    }
}
