using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class RadiologyInput : PhysicianInput
{
    [Required, StringLength(RadiologyStudy.AccessionLength), Display(Name = "Accession number")] public string AccessionNumber { get; set; } = "";
    [Required, StringLength(RadiologyStudy.NameLength), Display(Name = "Study name")] public string StudyName { get; set; } = "";
    [EnumDataType(typeof(RadiologyModality))] public RadiologyModality Modality { get; set; }
    [Required, StringLength(RadiologyStudy.NameLength), Display(Name = "Body region")] public string BodyRegion { get; set; } = "";
    [Required, Display(Name = "Performed at (Manila)")] public DateTime? PerformedAt { get; set; }
    [Display(Name = "Released to patient at (Manila)")] public DateTime? ReleasedAt { get; set; }
    [EnumDataType(typeof(RadiologyStatus))] public RadiologyStatus Status { get; set; } = RadiologyStatus.InProgress;
    [Display(Name = "Encounter")] public int? ClinicalEncounterId { get; set; }
    [Required] public PhysicianChoice Radiologist { get; set; } = new();
    [StringLength(RadiologyStudy.NameLength), Display(Name = "Performing unit")] public string? PerformingUnit { get; set; }
    [StringLength(RadiologyStudy.ShortTextLength), Display(Name = "Clinical indication")] public string? ClinicalIndication { get; set; }
    [StringLength(RadiologyStudy.ShortTextLength), Display(Name = "Technique")] public string? Technique { get; set; }
    [StringLength(RadiologyStudy.ShortTextLength), Display(Name = "Comparison")] public string? Comparison { get; set; }
    [StringLength(RadiologyStudy.FindingsLength), Display(Name = "Findings")] public string? Findings { get; set; }
    [StringLength(RadiologyStudy.ReportTextLength), Display(Name = "Impression")] public string? Impression { get; set; }
    [StringLength(RadiologyStudy.ReportTextLength), Display(Name = "Summary in plain language (optional)")] public string? PlainLanguageSummary { get; set; }
    [StringLength(RadiologyStudy.ShortTextLength), Display(Name = "Amendment note")] public string? AmendmentNote { get; set; }
    [StringLength(RadiologyStudy.ReportTextLength), Display(Name = "Internal notes (staff only)")] public string? InternalNotes { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (Status is RadiologyStatus.Final or RadiologyStatus.Amended)
        {
            if (ReleasedAt is null) yield return new ValidationResult("A final or amended report needs a release time.", [nameof(ReleasedAt)]);
            if (string.IsNullOrWhiteSpace(Findings)) yield return new ValidationResult("A final or amended report needs findings.", [nameof(Findings)]);
            if (string.IsNullOrWhiteSpace(Impression)) yield return new ValidationResult("A final or amended report needs an impression.", [nameof(Impression)]);
        }
        if (Status == RadiologyStatus.Amended && string.IsNullOrWhiteSpace(AmendmentNote))
            yield return new ValidationResult("An amended report needs an amendment note.", [nameof(AmendmentNote)]);
        if (ReleasedAt < PerformedAt) yield return new ValidationResult("Release cannot precede the exam.", [nameof(ReleasedAt)]);
        if (PerformedAt is { Kind: not DateTimeKind.Unspecified } || ReleasedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(PerformedAt)]);
    }
}
