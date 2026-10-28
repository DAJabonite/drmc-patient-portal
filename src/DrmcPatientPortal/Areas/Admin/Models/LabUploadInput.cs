using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class LabUploadInput : AdminInput, IValidatableObject
{
    [Display(Name = "Lab report PDF")] public IFormFile? Report { get; set; }
    [Required, Display(Name = "Test date (Manila)")] public DateTime? CollectedAt { get; set; }
    [Required, RegularExpression("^(Available|In progress|Pending Verification)$")]
    public string Status { get; set; } = "Pending Verification";
    [Display(Name = "Released at (Manila)")] public DateTime? ReleasedAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Status == "Available" && ReleasedAt is null)
            yield return new ValidationResult("Enter the release time for an available report.", [nameof(ReleasedAt)]);
        if (ReleasedAt < CollectedAt)
            yield return new ValidationResult("Release cannot precede collection.", [nameof(ReleasedAt)]);
        if (CollectedAt is { Kind: not DateTimeKind.Unspecified } || ReleasedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(CollectedAt)]);
    }
}
