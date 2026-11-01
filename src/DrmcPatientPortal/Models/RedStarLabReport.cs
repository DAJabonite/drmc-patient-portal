using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

// Private clinic results never enter the general laboratory table or staff worklists.
public sealed class RedStarLabReport
{
    public int Id { get; set; }
    public int RedStarPatientId { get; set; }
    public RedStarPatient RedStarPatient { get; set; } = null!;
    public int PatientRecordId { get; set; }
    public PatientRecord Patient { get; set; } = null!;
    public int ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    [Required, StringLength(200)] public string Description { get; set; } = "";
    public DateTime? CollectedAt { get; set; }
    [Required, StringLength(40)] public string Status { get; set; } = "Pending Verification";
    public DateTime? ReleasedAt { get; set; }
    public DateTime? PatientActivityAtUtc { get; set; }
    public string? ReportFileName { get; set; }
    public string? ReportOriginalFileNameProtected { get; set; }
    public string? ReportContentType { get; set; }
    public string? ReportSha256 { get; set; }
    public long? ReportSize { get; set; }
    public DateTime? ReportUploadedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
