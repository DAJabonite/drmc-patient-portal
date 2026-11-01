using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class LabUploadInput : AdminInput, IValidatableObject
{
    [Display(Name = "Encounter Reference")] public int? ClinicalEncounterId { get; set; }
    [Required, StringLength(200), Display(Name = "Exam type / description")] public string TestName { get; set; } = "";
    [Display(Name = "Lab report PDF")] public IFormFile? Report { get; set; }
    [Display(Name = "Actual test date (Manila)")] public DateTime? CollectedAt { get; set; }
    [Required, RegularExpression("^(Available|In progress|Pending Verification|Cancelled)$")]
    public string Status { get; set; } = "In progress";
    [Display(Name = "Released at (Manila)")] public DateTime? ReleasedAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Status == "Available" && ReleasedAt is null)
            yield return new ValidationResult("Enter the release time for an available report.", [nameof(ReleasedAt)]);
        if ((Report is not null || Status == "Available") && CollectedAt is null)
            yield return new ValidationResult("Enter the actual test date before uploading or releasing a report.", [nameof(CollectedAt)]);
        if (ReleasedAt < CollectedAt)
            yield return new ValidationResult("Release cannot precede collection.", [nameof(ReleasedAt)]);
        if (CollectedAt is { Kind: not DateTimeKind.Unspecified } || ReleasedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(CollectedAt)]);
    }
}
