using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class LabReportStorageOptions
{
    public string RootPath { get; set; } = "App_Data/LabReports";
}

public sealed record StoredLabReport(string FileName, long Size);

public interface ILabReportStorage
{
    Task<StoredLabReport> StoreAsync(int labId, IFormFile file, CancellationToken token);
    Task<byte[]> ReadAsync(int labId, string fileName, CancellationToken token);
    void Delete(string fileName);
}

public sealed class LabReportStorage : ILabReportStorage
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private readonly string root;
    private readonly IDataProtectionProvider protection;

    public LabReportStorage(IWebHostEnvironment environment, IOptions<LabReportStorageOptions> options,
        IDataProtectionProvider protection)
    {
        this.protection = protection;
        root = Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lab report storage must be outside wwwroot.");
    }

    public async Task<StoredLabReport> StoreAsync(int labId, IFormFile file, CancellationToken token)
    {
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
            file.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Choose a PDF file no larger than 10 MiB.");
        await using var input = file.OpenReadStream();
        var bytes = await ReadBoundedAsync(input, MaximumBytes, token);
        if (bytes.LongLength != file.Length || !bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("The file must have a PDF signature and match its declared size.");
        // Format checks are not malware scanning. The original filename is never persisted.
        var name = Guid.NewGuid().ToString("N") + ".pdf";
        Directory.CreateDirectory(root);
        var encrypted = Protector(labId).Protect(bytes);
        await using (var target = new FileStream(Resolve(name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await target.WriteAsync(encrypted, token);
        return new(name, bytes.LongLength);
    }

    public async Task<byte[]> ReadAsync(int labId, string fileName, CancellationToken token)
    {
        await using var input = new FileStream(Resolve(fileName), FileMode.Open, FileAccess.Read, FileShare.Read);
        var encrypted = await ReadBoundedAsync(input, MaximumBytes + 4096, token);
        var bytes = Protector(labId).Unprotect(encrypted);
        if (bytes.Length > MaximumBytes || !bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("The stored report is invalid.");
        return bytes;
    }

    public void Delete(string fileName) => File.Delete(Resolve(fileName));

    private IDataProtector Protector(int labId) => protection.CreateProtector("DRMC.LabReports.v1", labId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private string Resolve(string name)
    {
        if (name.Length != 36 || !name.EndsWith(".pdf", StringComparison.Ordinal) || !Guid.TryParseExact(name[..32], "N", out _))
            throw new InvalidDataException("Invalid report reference.");
        return Path.Combine(root, name);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, int limit, CancellationToken token)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("The report exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return output.ToArray();
    }
}
