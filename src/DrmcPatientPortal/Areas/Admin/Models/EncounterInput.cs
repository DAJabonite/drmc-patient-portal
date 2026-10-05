using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class EncounterInput : PhysicianInput
{
    [Required, StringLength(450), Display(Name = "Encounter reference")]
    public string EncounterReference { get; set; } = "";
    [Required, Display(Name = "Encounter date (Manila)")]
    public DateTime? EncounterDate { get; set; }
    [Required, StringLength(100000)] public string Department { get; set; } = "";
    [EnumDataType(typeof(EncounterType))] public EncounterType Type { get; set; }
    [StringLength(100000), Display(Name = "Chief complaint")] public string? ChiefComplaint { get; set; }
    [StringLength(100000), Display(Name = "Primary diagnosis")] public string? PrimaryDiagnosis { get; set; }
    [StringLength(100000), Display(Name = "Secondary diagnosis")] public string? SecondaryDiagnosis { get; set; }
    [StringLength(100000), Display(Name = "Clinical summary")] public string? ClinicalSummary { get; set; }
    [StringLength(100000), Display(Name = "Care plan and instructions")] public string? CarePlanAndInstructions { get; set; }
    [StringLength(100000), Display(Name = "Vital signs recorded")] public string? VitalSignsRecorded { get; set; }
    [Display(Name = "Follow-up date (Manila)")] public DateTime? FollowUpDate { get; set; }
    [StringLength(100000), Display(Name = "Follow-up notes")] public string? FollowUpNotes { get; set; }
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext)) yield return error;
        if (!ClinicalDepartments.All.Any(d => d.Name == Department))
            yield return new ValidationResult("Select a clinical department.", [nameof(Department)]);
        if (FollowUpDate < EncounterDate)
            yield return new ValidationResult("Follow-up cannot precede the encounter.", [nameof(FollowUpDate)]);
        if (EncounterDate is { Kind: not DateTimeKind.Unspecified } || FollowUpDate is { Kind: not DateTimeKind.Unspecified })
            yield return new ValidationResult("Enter Manila wall time without a time-zone suffix.", [nameof(EncounterDate)]);
    }
}
