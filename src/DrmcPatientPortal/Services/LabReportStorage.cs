using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class LabReportStorageOptions
{
    public string RootPath { get; set; } = "App_Data/LabReports";
}

public sealed record StoredLabReport(string FileName, long Size)
{
    public string? OriginalFileNameProtected { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
}

public interface ILabReportStorage
{
    Task<StoredLabReport> StoreAsync(int labId, IFormFile file, CancellationToken token);
    Task<byte[]> ReadAsync(int labId, string fileName, CancellationToken token);
    void Delete(string fileName);
}

public sealed class LabReportStorage : ILabReportStorage
{
    public const int MaximumBytes = PdfReportStorage.MaximumBytes;
    private readonly PdfReportStorage storage;

    public LabReportStorage(IWebHostEnvironment environment, IOptions<LabReportStorageOptions> options,
        IDataProtectionProvider protection)
    {
        storage = new(environment, options.Value.RootPath, protection, KeyRingProtection.LabReportPurpose);
    }

    public Task<StoredLabReport> StoreAsync(int labId, IFormFile file, CancellationToken token) => storage.StoreAsync(labId, file, token);

    public Task<byte[]> ReadAsync(int labId, string fileName, CancellationToken token) => storage.ReadAsync(labId, fileName, token);
    public void Delete(string fileName) => storage.Delete(fileName);
}
