using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class RedStarHospitalMappingInput
{
    [Required, StringLength(80)] public string HospitalNumber { get; set; } = "";
    [Required, StringLength(255)] public string LastName { get; set; } = "";
    [Required, StringLength(255)] public string FirstName { get; set; } = "";
    [StringLength(255)] public string? MiddleName { get; set; }
    [StringLength(80)] public string? Suffix { get; set; }
    [Required, DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
    public bool IdentityVerified { get; set; }
    public int HospitalPatientRecordId { get; set; }
    public string? HospitalVersion { get; set; }
    [Required] public string PatientVersion { get; set; } = "";
}

public sealed class RedStarLabUploadRow
{
    [StringLength(200)] public string? Description { get; set; }
    public IFormFile? Report { get; set; }
    public int? PendingRequestId { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class RedStarLabUploadInput
{
    public int ClinicalEncounterId { get; set; }
    [DataType(DataType.Date)] public DateTime? CollectedAt { get; set; }
    [RegularExpression("^(In progress|Pending Verification|Available)$")] public string Status { get; set; } = "Pending Verification";
    public DateTime? ReleasedAt { get; set; }
    [Required] public string PatientVersion { get; set; } = "";
    public List<RedStarLabUploadRow> Rows { get; set; } = [new()];
}

public sealed class RedStarLabEditInput
{
    [Required] public string RowVersion { get; set; } = "";
    [Required, StringLength(200)] public string Description { get; set; } = "";
    public int ClinicalEncounterId { get; set; }
    public DateTime? CollectedAt { get; set; }
    [RegularExpression("^(In progress|Pending Verification|Available|Cancelled|Withdrawn)$")] public string Status { get; set; } = "Pending Verification";
    public DateTime? ReleasedAt { get; set; }
    public IFormFile? Report { get; set; }
    public bool RemoveReport { get; set; }
    [StringLength(1000)] public string? CorrectionReason { get; set; }
}

public sealed record RedStarLabPage(RedStarPatient Clinic, PatientRecord? Hospital,
    IReadOnlyList<RedStarLabReport> Reports, int Page, int Total);
public sealed record RedStarMappingPage(RedStarPatient Clinic, RedStarHospitalMappingInput Input, PatientRecord? Candidate);
public sealed record RedStarUploadPage(RedStarPatient Clinic, PatientRecord Hospital,
    IReadOnlyList<ClinicalEncounter> Encounters, IReadOnlyList<RedStarLabReport> Pending, RedStarLabUploadInput Input);
public sealed record RedStarEditReportPage(RedStarLabReport Report, PatientRecord Hospital,
    IReadOnlyList<ClinicalEncounter> Encounters, RedStarLabEditInput Input);

public sealed record RedStarClinicPickerPage(IReadOnlyList<RedStarPatient> Patients, int Page, int Total);
