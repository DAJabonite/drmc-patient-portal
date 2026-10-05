using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class AllergyInput : AdminInput, IValidatableObject
{
    [Required, StringLength(100000)] public string Allergen { get; set; } = "";
    [StringLength(100000)] public string? Reaction { get; set; }
    [EnumDataType(typeof(AllergySeverity))] public AllergySeverity Severity { get; set; } = AllergySeverity.Moderate;
    [Required, Display(Name = "Recorded at (Manila)")] public DateTime? RecordedAt { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RecordedAt is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(RecordedAt)]);
    }
}
