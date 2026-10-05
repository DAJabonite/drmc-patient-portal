using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class LabInput : PhysicianInput
{
    [Required, StringLength(100000), Display(Name = "Accession number")] public string AccessionNumber { get; set; } = "";
    [Required, StringLength(100000), Display(Name = "Test name")] public string TestName { get; set; } = "";
    [EnumDataType(typeof(LabCategory))] public LabCategory Category { get; set; }
    [Required, Display(Name = "Collected at (Manila)")] public DateTime? CollectedAt { get; set; }
    [Display(Name = "Released at (Manila)")] public DateTime? ReleasedAt { get; set; }
    [Required, RegularExpression("^(Available|In progress|Pending Verification)$")] public string Status { get; set; } = "Pending Verification";
    [Display(Name = "Encounter")] public int? ClinicalEncounterId { get; set; }
    [StringLength(100000), Display(Name = "Result summary")] public string? ResultSummary { get; set; }
    [Required] public PhysicianChoice Pathologist { get; set; } = new();
    [StringLength(100000), Display(Name = "Performing unit")] public string? PerformingUnit { get; set; }
    [StringLength(100000), Display(Name = "Clinical notes")] public string? ClinicalNotes { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (ReleasedAt < CollectedAt) yield return new ValidationResult("Release cannot precede collection.", [nameof(ReleasedAt)]);
        if (CollectedAt is { Kind: not DateTimeKind.Unspecified } || ReleasedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(CollectedAt)]);
    }
}
