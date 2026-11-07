using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class PrescriptionInput : PhysicianInput
{
    [Display(Name = "Encounter Reference (Case Number)")] public int? ClinicalEncounterId { get; set; }
    [Required, StringLength(450), Display(Name = "Rx number")] public string RxNumber { get; set; } = "";
    [Required, StringLength(100000), Display(Name = "Generic name")] public string GenericName { get; set; } = "";
    [StringLength(100000), Display(Name = "Brand name")] public string? BrandName { get; set; }
    [StringLength(100000)] public string? Dosage { get; set; }
    [StringLength(100000), Display(Name = "Dosage form")] public string? DosageForm { get; set; }
    [StringLength(32), RegularExpression(AdministrationRoutes.Pattern, ErrorMessage = "Select a listed route of administration."),
        Display(Name = "Route of administration")] public string? RouteOfAdministration { get; set; }
    [StringLength(100000)] public string? Frequency { get; set; }
    [StringLength(100000)] public string? Instructions { get; set; }
    [Required, StringLength(100000)] public string Department { get; set; } = "";
    [Required, Display(Name = "Prescribed at (Manila)")] public DateTime? PrescribedAt { get; set; }
    [Required, Display(Name = "Valid until (Manila)")] public DateTime? ValidUntil { get; set; }
    [EnumDataType(typeof(PrescriptionStatus))] public PrescriptionStatus Status { get; set; }
    [EnumDataType(typeof(PrescriptionFulfillmentStatus))] public PrescriptionFulfillmentStatus FulfillmentStatus { get; set; }
    [EnumDataType(typeof(PrescriptionFrequencyMode))] public PrescriptionFrequencyMode? FrequencyMode { get; set; }
    public decimal? FrequencyValue { get; set; }
    public decimal? DosePerIntake { get; set; }
    [StringLength(40)] public string? DoseUnit { get; set; }
    public int? DurationCount { get; set; }
    [EnumDataType(typeof(PrescriptionDurationUnit))] public PrescriptionDurationUnit? DurationUnit { get; set; }
    public decimal? PrescribedQuantity { get; set; }
    public decimal? DispensedQuantity { get; set; }
    public DateTime? ServedDate { get; set; }
    public bool IsRedStar { get; set; }
    public int? RedStarEnrollmentId { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (!ClinicalDepartments.All.Any(d => d.Name == Department)) yield return new ValidationResult("Select a clinical department.", [nameof(Department)]);
        if (ValidUntil < PrescribedAt) yield return new ValidationResult("Validity cannot end before the prescription date.", [nameof(ValidUntil)]);
        if (PrescribedAt is { Kind: not DateTimeKind.Unspecified } || ValidUntil is { Kind: not DateTimeKind.Unspecified } || ServedDate is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(PrescribedAt)]);
        if (PrescriptionCalculations.HasRegimen(this))
        {
            var result = PrescriptionCalculations.Calculate(this, ClinicalClock.Today);
            if (result.Error is not null) yield return new ValidationResult(result.Error);
        }
    }
}
