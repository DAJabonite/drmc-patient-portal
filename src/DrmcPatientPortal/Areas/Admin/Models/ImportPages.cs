using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class ImportUpload
{
    [Required] public IFormFile? File { get; set; }
    [Required] public string Template { get; set; } = "";
}

public sealed class ImportMapping
{
    [Required, StringLength(200)] public string SourcePatientKey { get; set; } = "";
    public int? PatientRecordId { get; set; }
    public bool CreateNew { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] public PatientInput Patient { get; set; } = new();
}

public sealed class ImportDryRun
{
    [Required, StringLength(12)] public string RowVersion { get; set; } = "";
    public List<ImportMapping> Mappings { get; set; } = [];
}

public sealed class ImportApproval
{
    [Required, StringLength(12)] public string RowVersion { get; set; } = "";
    [Required, StringLength(64)] public string Hash { get; set; } = "";
    [Range(typeof(bool), "true", "true")] public bool Confirm { get; set; }
}

public sealed record ImportRow(string Template, int Number, Dictionary<string, string> Fields);
public sealed record ImportRowReview(string Template, int Number, string PatientKey, string Reference, string Decision, IReadOnlyList<string> Errors);
public sealed record ImportGroup(string Key, string Name, string BirthDate, int Rows, bool FromPatientTemplate, string HospitalNumber);
public sealed record ImportReport(List<ImportRowReview> Rows, Dictionary<string, int> Counts, string Hash)
{
    public bool Valid => Rows.All(row => row.Errors.Count == 0);
}
public sealed class ImportEnvelope
{
    public byte[] Content { get; set; } = [];
    public string Format { get; set; } = "";
    public string Template { get; set; } = "";
    public List<ImportMapping> Mappings { get; set; } = [];
    public ImportReport? Report { get; set; }
}
public sealed record ImportCommand(string Nonce, string FileHash, string Version, List<ImportMapping> Mappings);
public sealed record ImportIndex(IReadOnlyList<ImportBatch> Rows, int Page, int Total);
public sealed record ImportDetail(ImportBatch Batch, ImportReport? Report, int Page, bool ReportVerified = false);
public sealed record ImportReconciliation(ImportBatch Batch, IReadOnlyList<ImportGroup> Groups, ImportDryRun Input, int Page, int TotalGroups);
