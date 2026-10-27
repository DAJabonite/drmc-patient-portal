using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public enum RedStarImportFormat { Xlsx, Csv }

public sealed class RedStarImportPreview
{
    public RedStarImportFormat SourceFormat { get; set; } = RedStarImportFormat.Xlsx;
    public List<string> SkippedSheets { get; set; } = [];
    public List<RedStarImportRow> Rows { get; set; } = [];
    public List<string> DeferredColumns { get; set; } = [];
    public int IgnoredRows { get; set; }
    public List<RedStarImportChoice> Choices { get; set; } = [];
}

public sealed class RedStarImportRow
{
    public string Key { get; set; } = "";
    public string Sheet { get; set; } = "";
    public int Number { get; set; }
    public string FullName { get; set; } = "";
    public string PatientCode { get; set; } = "";
    public string CodeName { get; set; } = "";
    public string? HospitalNumber { get; set; }
    public string? ContactNumber { get; set; }
    public string? Address { get; set; }
    public string Medicines { get; set; } = "";
    public RedStarProgram Program { get; set; }
    public RedStarStatus Status { get; set; }
    public string? SourceStatus { get; set; }
    public string? ReceivingHub { get; set; }
    public DateTime? EnrolledDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? StatusDate { get; set; }
    public DateTime? DispensedDate { get; set; }
    public DateTime? NextRefillDate { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class RedStarImportChoice
{
    [Required, StringLength(160)] public string Key { get; set; } = "";
    public bool Include { get; set; }
    public int? ExistingPatientId { get; set; }
    public string? ExistingPatientVersion { get; set; }
    public int? ExistingEnrollmentId { get; set; }
    public string? ExistingEnrollmentVersion { get; set; }
    public string? ExistingScheduleVersion { get; set; }
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [Required, StringLength(80)] public string PatientCode { get; set; } = "";
    [StringLength(120)] public string CodeName { get; set; } = "";
    [StringLength(80)] public string? HospitalNumber { get; set; }
    [StringLength(40)] public string? ContactNumber { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(2000)] public string Medicines { get; set; } = "";
    public RedStarProgram Program { get; set; }
    public RedStarStatus Status { get; set; }
    [StringLength(200)] public string? ReceivingHub { get; set; }
    public DateTime? EnrolledDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? StatusDate { get; set; }
    public DateTime? DispensedDate { get; set; }
    public DateTime? NextRefillDate { get; set; }
}

public sealed class RedStarReconcileInput
{
    [Required] public string Version { get; set; } = "";
    public int Page { get; set; } = 1;
    public List<RedStarImportChoice> Choices { get; set; } = [];
}

public sealed record RedStarImportIndex(IReadOnlyList<RedStarImportBatch> Batches, int Page, int Total);
public sealed record RedStarImportCandidate(int Id, string PatientCode, string CodeName, string Version);
public sealed record RedStarImportReview(RedStarImportBatch Batch, RedStarImportPreview Preview,
    IReadOnlyList<RedStarImportRow> Rows, IReadOnlyList<RedStarImportChoice> Choices,
    IReadOnlyDictionary<string, IReadOnlyList<RedStarImportCandidate>> Candidates, int Page);
