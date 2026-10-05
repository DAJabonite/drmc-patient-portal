using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class PatientInput : AdminInput, IValidatableObject
{
    [Required, StringLength(100000), Display(Name = "Full name")]
    public string FullName { get; set; } = "";
    [DataType(DataType.Date), Display(Name = "Birth date")]
    public DateTime? DateOfBirth { get; set; }
    [StringLength(450), Display(Name = "Hospital number")]
    public string? HospitalNumber { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth?.Date > ClinicalClock.Today)
            yield return new ValidationResult("Birth date cannot be in the future.", [nameof(DateOfBirth)]);
    }
}

public sealed class PatientLinkInput : AdminInput
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that staff checked the patient's identity.")]
    public bool IdentityVerified { get; set; }
}
public sealed record PatientLinkPage(OwnershipContext Context, PatientLinkInput Input, bool Linked);
