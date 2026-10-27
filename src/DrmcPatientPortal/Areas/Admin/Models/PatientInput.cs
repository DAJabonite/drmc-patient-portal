using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class PatientInput : AdminInput, IValidatableObject
{
    [Required, StringLength(60), Display(Name = "Last name")]
    public string LastName { get; set; } = "";
    [Required, StringLength(60), Display(Name = "First name")]
    public string FirstName { get; set; } = "";
    [StringLength(60), Display(Name = "Middle name (optional)")]
    public string? MiddleName { get; set; }
    [StringLength(10), Display(Name = "Suffix (optional)")]
    public string? Suffix { get; set; }
    [DataType(DataType.Date), Display(Name = "Birth date")]
    public DateTime? DateOfBirth { get; set; }
    [Required(ErrorMessage = "Enter a hospital number."), StringLength(450), Display(Name = "Hospital number")]
    public string? HospitalNumber { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth?.Date > ClinicalClock.Today)
            yield return new ValidationResult("Birth date cannot be in the future.", [nameof(DateOfBirth)]);
    }
}

public abstract class PatientAccountInput : AdminInput
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that staff checked the patient's identity.")]
    public bool IdentityVerified { get; set; }
}

public sealed class PatientLinkInput : PatientAccountInput
{
    [Required(ErrorMessage = "Enter a hospital number."), StringLength(450), Display(Name = "Hospital number")]
    public string? HospitalNumber { get; set; }
    [Required(ErrorMessage = "Enter the hospital record code."), StringLength(64), Display(Name = "Hospital record code")]
    public string? HospitalRecordCode { get; set; }
}

public sealed class PatientUnlinkInput : PatientAccountInput;

public sealed record PatientLinkPage(OwnershipContext Context, PatientLinkInput Input, PatientUnlinkInput UnlinkInput, bool Linked);
