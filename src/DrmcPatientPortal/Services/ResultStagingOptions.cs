using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class ResultStagingOptions
{
    public const string SectionName = "ResultStaging";
    public bool Enabled { get; set; }
    public string LaboratoryPath { get; set; } = "App_Data/ResultStaging/Laboratory";
    public string RadiologyPath { get; set; } = "App_Data/ResultStaging/Radiology";
    public string QuarantinePath { get; set; } = "App_Data/ResultStaging/Quarantine";
    public int PollIntervalSeconds { get; set; } = 30;
}

public sealed class ResultStagingOptionsValidator(IWebHostEnvironment environment, IConfiguration configuration)
    : IValidateOptions<ResultStagingOptions>
{
    public ValidateOptionsResult Validate(string? name, ResultStagingOptions options)
    {
        if (options.PollIntervalSeconds is < 1 or > 3600)
            return ValidateOptionsResult.Fail("ResultStaging:PollIntervalSeconds must be between 1 and 3600.");
        if (!options.Enabled) return ValidateOptionsResult.Success;
        try
        {
            var staged = new[] { options.LaboratoryPath, options.RadiologyPath, options.QuarantinePath }
                .Select(p => PrivateStorageRoot.ValidatePath(environment, p)).ToArray();
            var other = new[]
            {
                configuration["LabReports:RootPath"] ?? "App_Data/LabReports",
                configuration["RadiologyReports:RootPath"] ?? "App_Data/RadiologyReports",
                configuration["PatientDocuments:RootPath"] ?? "App_Data/PatientIdDocuments",
                configuration["PatientDocuments:TemporaryPath"] ?? "App_Data/TempUploads",
                configuration["DataProtection:KeyRingPath"] ?? "App_Data/DataProtectionKeys",
                configuration["AdvisoryImages:RootPath"] ?? "App_Data/AdvisoryImages",
                configuration["Imports:StagingPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMC", "ImportStaging")
            }.Select(p => PrivateStorageRoot.ValidatePath(environment, p)).ToArray();
            if (staged.Where((path, index) => staged.Skip(index + 1).Any(p => PrivateStorageRoot.PathsOverlap(path, p))).Any()
                || staged.Any(path => other.Any(p => PrivateStorageRoot.PathsOverlap(path, p)))
                || PrivateStorageRoot.PathsOverlap(other[0], other[1]))
                return ValidateOptionsResult.Fail("Result staging, quarantine and private storage directories must be separate.");
            return ValidateOptionsResult.Success;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        { return ValidateOptionsResult.Fail("Result staging paths must be valid private directories."); }
    }
}
