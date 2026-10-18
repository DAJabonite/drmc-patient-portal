using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DrmcPatientPortal.Services;

// The catalog is shared by the EF mapping and the offline upgrader: a field cannot be
// upgraded with a different purpose from the one used by ordinary application reads.
public sealed record RedStarProtectedTable(Type Entity, string Table, params string[] Fields);

public sealed class RedStarPhiProtection(IDataProtectionProvider provider, string keyRingPath) : IDisposable
{
    public const string Envelope = "rsphi:v1:";
    public static readonly RedStarProtectedTable[] Tables =
    [
        new(typeof(RedStarPatient), "RedStarPatients", "PatientCode", "CodeName", "FullName", "HospitalNumber", "ContactNumber", "Address", "PortalUserId"),
        new(typeof(RedStarEnrollment), "RedStarEnrollments", "ReceivingHub", "Medicines", "SourceStatus"),
        new(typeof(RedStarAppointment), "RedStarAppointments", "Label"),
        new(typeof(RedStarDispense), "RedStarDispenses", "Medicines", "RecordedById"),
        new(typeof(RedStarRescheduleRequest), "RedStarRescheduleRequests", "Reason"),
        new(typeof(RedStarReminder), "RedStarReminders", "PortalUserId"),
        new(typeof(RedStarAudit), "RedStarAudits", "ActorId"),
        new(typeof(RedStarImportBatch), "RedStarImportBatches", "CreatedById")
    ];
    private readonly object gate = new();
    private byte[]? key;
    public string BlindKeyPath => Path.Combine(keyRingPath, "redstar-blind-index-v1.protected");
    private IDataProtector KeyProtector => provider.CreateProtector(KeyRingProtection.RedStarPhiPurpose, "BlindIndexKey.v1");
    private IDataProtector Protector(string entity, string field) => provider.CreateProtector(KeyRingProtection.RedStarPhiPurpose, entity, field);

    public string Protect(string entity, string field, string value) => Envelope + Protector(entity, field).Protect(value);
    public string Unprotect(string entity, string field, string value)
    {
        if (!value.StartsWith(Envelope, StringComparison.Ordinal)) throw new CryptographicException("Invalid private field protection version.");
        return Protector(entity, field).Unprotect(value[Envelope.Length..]);
    }
    public ValueConverter<string, string> Converter(string entity, string field) => new(
        value => Protect(entity, field, value), value => Unprotect(entity, field, value));

    public static string NormalizeCode(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    public string PatientCodeIndex(string value) => Index("PatientCode", NormalizeCode(value));
    public string? HospitalNumberIndex(string? value) => value is null ? null : Index("HospitalNumber", NormalizeCode(value));
    public string? AccountIndex(string? value) => value is null ? null : Index("PortalAccount", value);
    public string KeyCheck => Index("KeyVerification.v1", "DRMC.RedStar");
    private string Index(string domain, string value)
    {
        lock (gate)
        {
            key ??= ReadKey();
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(domain + "\0" + value)));
        }
    }
    private byte[] ReadKey()
    {
        if (!File.Exists(BlindKeyPath)) throw new InvalidOperationException("The private registry lookup key is unavailable. Run the offline upgrade or restore its original key file.");
        var bytes = KeyProtector.Unprotect(File.ReadAllBytes(BlindKeyPath));
        if (bytes.Length != 32) throw new CryptographicException("Invalid private registry lookup key.");
        return bytes;
    }
    // Called only by the maintenance upgrade, and only when no key binding exists.
    public void InitializeKey()
    {
        Directory.CreateDirectory(keyRingPath);
        using var fileGate = new FileStream(BlindKeyPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(BlindKeyPath)) { _ = KeyCheck; return; }
        var secret = RandomNumberGenerator.GetBytes(32);
        var temporary = BlindKeyPath + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(KeyProtector.Protect(secret));
                output.Flush(true);
            }
            File.Move(temporary, BlindKeyPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public string CreateCanary() => Protector("ProtectionState", "Canary").Protect("DRMC.RedStar.Ready.v1");
    public void VerifyCanary(string canary)
    {
        if (Protector("ProtectionState", "Canary").Unprotect(canary) != "DRMC.RedStar.Ready.v1")
            throw new CryptographicException("Private registry key verification failed.");
    }
    public static readonly CompareInfo RegistryComparison = CultureInfo.GetCultureInfo("en-US").CompareInfo;
    public static readonly IComparer<string> RegistryOrder = Comparer<string>.Create((a, b) => RegistryComparison.Compare(a, b, CompareOptions.None));
    public static bool Contains(string value, string search) => RegistryComparison.IndexOf(value, search, CompareOptions.IgnoreCase) >= 0;
    public void Dispose() { lock (gate) { if (key is not null) CryptographicOperations.ZeroMemory(key); key = null; } }
}

public sealed class RedStarModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is ApplicationDbContext db
        ? (context.GetType(), db.ProtectionIdentity, designTime) : (object)(context.GetType(), designTime);
}
