using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class DoctorInput : IValidatableObject
{
    [Required, StringLength(100000), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;
    [Required, StringLength(100000)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(450)] public string Department { get; set; } = string.Empty;
    [StringLength(100000), Display(Name = "Subspecialty")]
    public string? SubSpecialty { get; set; }
    [StringLength(100000), Display(Name = "Clinic room")]
    public string? ClinicRoom { get; set; }
    [StringLength(100000), Display(Name = "Schedule")]
    public string? ScheduleSummary { get; set; }
    [Display(Name = "Offers teleconsultation")] public bool OffersTeleconsult { get; set; }
    [StringLength(100000)] public string? Biography { get; set; }
    [StringLength(100000), Display(Name = "Masked PRC license")]
    public string? PrcLicenseMasked { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
    [StringLength(12)] public string? RowVersion { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!ClinicalDepartments.All.Any(d => d.Name == Department))
            yield return new ValidationResult("Select a listed department.", [nameof(Department)]);
    }

    public void Apply(Doctor doctor)
    {
        doctor.FullName = FullName.Trim(); doctor.Title = Title.Trim(); doctor.Department = Department;
        doctor.SubSpecialty = SubSpecialty ?? ""; doctor.ClinicRoom = ClinicRoom ?? "";
        doctor.ScheduleSummary = ScheduleSummary ?? ""; doctor.OffersTeleconsult = OffersTeleconsult;
        doctor.Biography = Biography ?? ""; doctor.PrcLicenseMasked = PrcLicenseMasked ?? ""; doctor.IsActive = IsActive;
    }

    public static DoctorInput From(Doctor doctor, byte[] version) => new()
    {
        FullName = doctor.FullName, Title = doctor.Title, Department = doctor.Department,
        SubSpecialty = doctor.SubSpecialty, ClinicRoom = doctor.ClinicRoom, ScheduleSummary = doctor.ScheduleSummary,
        OffersTeleconsult = doctor.OffersTeleconsult, Biography = doctor.Biography,
        PrcLicenseMasked = doctor.PrcLicenseMasked, IsActive = doctor.IsActive, RowVersion = Convert.ToBase64String(version)
    };
}

public sealed record DoctorList(IReadOnlyList<Doctor> Rows, string? Search, int Page, int Total)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}
