using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class RedStarLabReportStorageOptions
{
    public string RootPath { get; set; } = "App_Data/RedStarLabReports";
}

public interface IRedStarLabReportStorage
{
    Task<StoredLabReport> StoreAsync(int reportId, IFormFile file, CancellationToken token);
    Task<byte[]> ReadAsync(int reportId, string fileName, CancellationToken token);
    void Delete(string fileName);
}

public sealed class RedStarLabReportStorage : IRedStarLabReportStorage
{
    public const string Purpose = "DRMC.RedStar.LabReports.v1";
    private readonly PdfReportStorage storage;
    public RedStarLabReportStorage(IWebHostEnvironment environment, IOptions<RedStarLabReportStorageOptions> options,
        IDataProtectionProvider protection) => storage = new(environment, options.Value.RootPath, protection, Purpose);
    public Task<StoredLabReport> StoreAsync(int reportId, IFormFile file, CancellationToken token) => storage.StoreAsync(reportId, file, token);
    public Task<byte[]> ReadAsync(int reportId, string fileName, CancellationToken token) => storage.ReadAsync(reportId, fileName, token);
    public void Delete(string fileName) => storage.Delete(fileName);
}
