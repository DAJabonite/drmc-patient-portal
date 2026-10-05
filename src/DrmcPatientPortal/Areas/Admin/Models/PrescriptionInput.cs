using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class PrescriptionInput : PhysicianInput
{
    [Required, StringLength(450), Display(Name = "Rx number")] public string RxNumber { get; set; } = "";
    [Required, StringLength(100000), Display(Name = "Generic name")] public string GenericName { get; set; } = "";
    [StringLength(100000), Display(Name = "Brand name")] public string? BrandName { get; set; }
    [StringLength(100000)] public string? Dosage { get; set; }
    [StringLength(100000), Display(Name = "Dosage form")] public string? DosageForm { get; set; }
    [StringLength(100000)] public string? Frequency { get; set; }
    [StringLength(100000)] public string? Instructions { get; set; }
    [Required, StringLength(100000)] public string Department { get; set; } = "";
    [Required, Display(Name = "Prescribed at (Manila)")] public DateTime? PrescribedAt { get; set; }
    [Required, Display(Name = "Valid until (Manila)")] public DateTime? ValidUntil { get; set; }
    [EnumDataType(typeof(PrescriptionStatus))] public PrescriptionStatus Status { get; set; }
    [Required, Range(0, int.MaxValue), Display(Name = "Total refills")] public int? RefillsTotal { get; set; }
    [Required, Range(0, int.MaxValue), Display(Name = "Remaining refills")] public int? RefillsRemaining { get; set; }
    [Display(Name = "Last refill date (Manila)")] public DateTime? LastRefillDate { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (!ClinicalDepartments.All.Any(d => d.Name == Department)) yield return new ValidationResult("Select a clinical department.", [nameof(Department)]);
        if (ValidUntil < PrescribedAt) yield return new ValidationResult("Validity cannot end before the prescription date.", [nameof(ValidUntil)]);
        if (RefillsRemaining > RefillsTotal) yield return new ValidationResult("Remaining refills cannot exceed the total.", [nameof(RefillsRemaining)]);
        if (LastRefillDate is not null && RefillsTotal == RefillsRemaining)
            yield return new ValidationResult("A last refill date requires a used refill.", [nameof(LastRefillDate)]);
        if (LastRefillDate < PrescribedAt || LastRefillDate > ValidUntil)
            yield return new ValidationResult("Last refill must fall within the prescription dates.", [nameof(LastRefillDate)]);
        if (PrescribedAt is { Kind: not DateTimeKind.Unspecified } || ValidUntil is { Kind: not DateTimeKind.Unspecified } || LastRefillDate is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(PrescribedAt)]);
    }
}
