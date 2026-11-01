using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class LabBatchInput
{
    [Required, Display(Name = "Encounter Reference")] public int? ClinicalEncounterId { get; set; }
    [Required, Display(Name = "Actual test date (Manila)")] public DateTime? CollectedAt { get; set; }
    [MinLength(1), MaxLength(10)] public List<IFormFile> Files { get; set; } = [];
    [MaxLength(10)] public List<LabBatchItemInput> Items { get; set; } = [];
}

public sealed class LabBatchItemInput
{
    [StringLength(200)] public string? Description { get; set; }
    [StringLength(40)] public string? ExistingRequest { get; set; }
}

public sealed class ResultAssignmentInput : AdminInput
{
    [Required, Display(Name = "Encounter Reference")] public int? ClinicalEncounterId { get; set; }
    [StringLength(40), Display(Name = "Fulfill an existing request")] public string? ExistingRequest { get; set; }
}

public sealed record PendingRequestChoice(string Key, string Label, int EncounterId);
public sealed record ResultAssignmentPage(int Id, string Description, string PatientName, string HospitalNumber,
    DateTime? ActualDate, ResultAssignmentInput Input, IReadOnlyList<PendingRequestChoice> Requests);
