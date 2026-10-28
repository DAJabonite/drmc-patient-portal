using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class RadiologyReportStorageOptions
{
    public string RootPath { get; set; } = "App_Data/RadiologyReports";
}

public interface IRadiologyReportStorage
{
    Task<StoredLabReport> StoreAsync(int studyId, IFormFile file, CancellationToken token);
    Task<byte[]> ReadAsync(int studyId, string fileName, CancellationToken token);
    void Delete(string fileName);
}

public sealed class RadiologyReportStorage(IWebHostEnvironment environment, IOptions<RadiologyReportStorageOptions> options,
    IDataProtectionProvider protection) : IRadiologyReportStorage
{
    private readonly PdfReportStorage storage = new(environment, options.Value.RootPath, protection, KeyRingProtection.RadiologyReportPurpose);
    public Task<StoredLabReport> StoreAsync(int studyId, IFormFile file, CancellationToken token) => storage.StoreAsync(studyId, file, token);
    public Task<byte[]> ReadAsync(int studyId, string fileName, CancellationToken token) => storage.ReadAsync(studyId, fileName, token);
    public void Delete(string fileName) => storage.Delete(fileName);
}
