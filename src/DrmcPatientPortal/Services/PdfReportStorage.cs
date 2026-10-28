using System.Globalization;
using System.Security.Cryptography;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;

namespace DrmcPatientPortal.Services;

// Shared validation and I/O; the existing laboratory purpose and file format stay unchanged.
internal sealed class PdfReportStorage(IWebHostEnvironment environment, string configuredPath,
    IDataProtectionProvider protection, string purpose)
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private readonly string path = PrivateStorageRoot.ValidatePath(environment, configuredPath);
    // Check physical access when used: an offline storage share must not block the other result kind's DI scope.
    private PrivateStorageRoot Root => new(environment, path);
    private IDataProtector Protector(int id) => protection.CreateProtector(purpose, id.ToString(CultureInfo.InvariantCulture));

    public async Task<StoredLabReport> StoreAsync(int id, IFormFile file, CancellationToken token)
    {
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
            file.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Choose a PDF file no larger than 10 MiB.");
        await using var input = file.OpenReadStream();
        var bytes = await PrivateStorageIO.ReadBoundedAsync(input, MaximumBytes, token);
        if (bytes.LongLength != file.Length || !bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("The file must have a PDF signature and match its declared size.");
        var original = file.FileName.Replace('\\', '/').Split('/')[^1];
        if (!PrivateStorageRoot.IsLeaf(original)) throw new InvalidDataException("Choose a PDF with a valid filename.");
        var protector = Protector(id);
        // Names may identify patients. They are only persisted in a separate protected envelope.
        var protectedName = protector.CreateProtector("OriginalFileName").Protect(original);
        var name = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Root.Resolve());
        await PrivateStorageIO.WriteNewAsync(Resolve(name), protector.Protect(bytes), token);
        return new(name, bytes.LongLength)
        {
            OriginalFileNameProtected = protectedName, ContentType = "application/pdf",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    public async Task<byte[]> ReadAsync(int id, string name, CancellationToken token)
    {
        var encrypted = await PrivateStorageIO.ReadAsync(Resolve(name), MaximumBytes + 4096, token);
        var bytes = Protector(id).Unprotect(encrypted);
        if (bytes.Length is 0 or > MaximumBytes || !bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("The stored report is invalid.");
        return bytes;
    }

    public void Delete(string name) => File.Delete(Resolve(name));
    private string Resolve(string name)
    {
        if (!PrivateStorageRoot.IsGuid(name) &&
            !(name?.Length == 36 && name.EndsWith(".pdf", StringComparison.Ordinal) && PrivateStorageRoot.IsGuid(name[..32])))
            throw new FileNotFoundException("Invalid report reference.");
        return Root.Resolve(name!);
    }
}

public static class ReportAttachments
{
    public static void Apply(LabResult result, StoredLabReport? report, DateTime utcNow)
    {
        result.ReportFileName = report?.FileName;
        result.ReportOriginalFileNameProtected = report?.OriginalFileNameProtected;
        result.ReportContentType = report?.ContentType;
        result.ReportSha256 = report?.Sha256;
        result.ReportSize = report?.Size;
        result.ReportUploadedAtUtc = report is null ? null : utcNow;
    }

    public static void Apply(RadiologyStudy result, StoredLabReport report, DateTime utcNow)
    {
        result.ReportFileName = report.FileName;
        result.ReportOriginalFileNameProtected = report.OriginalFileNameProtected;
        result.ReportContentType = report.ContentType;
        result.ReportSha256 = report.Sha256;
        result.ReportSize = report.Size;
        result.ReportUploadedAtUtc = utcNow;
    }
}
