using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class RadiologyInput : AdminInput, IValidatableObject
{
    [Display(Name = "Radiology report PDF")] public IFormFile? Report { get; set; }
    [EnumDataType(typeof(RadiologyModality))] public RadiologyModality Modality { get; set; }
    [Required, Display(Name = "Study date (Manila)")] public DateTime? PerformedAt { get; set; }
    [Display(Name = "Released to patient at (Manila)")] public DateTime? ReleasedAt { get; set; }
    [EnumDataType(typeof(RadiologyStatus))] public RadiologyStatus Status { get; set; } = RadiologyStatus.InProgress;
    [StringLength(RadiologyStudy.ShortTextLength), Display(Name = "Amendment note")] public string? AmendmentNote { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is RadiologyStatus.Final or RadiologyStatus.Amended)
        {
            if (ReleasedAt is null) yield return new ValidationResult("A final or amended report needs a release time.", [nameof(ReleasedAt)]);
        }
        if (Status == RadiologyStatus.Amended && string.IsNullOrWhiteSpace(AmendmentNote))
            yield return new ValidationResult("An amended report needs an amendment note.", [nameof(AmendmentNote)]);
        if (ReleasedAt < PerformedAt) yield return new ValidationResult("Release cannot precede the exam.", [nameof(ReleasedAt)]);
        if (PerformedAt is { Kind: not DateTimeKind.Unspecified } || ReleasedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(PerformedAt)]);
    }
}
