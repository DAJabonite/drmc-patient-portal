using System.ComponentModel.DataAnnotations;

namespace DrmcPatientPortal.Models;

// A diagnostic imaging study and its narrative report. Analyte rows (LabResultItem) do not fit
// narrative radiology reports, so imaging has its own table. PatientRecordId owns the record;
// the optional encounter must belong to the same patient. Times are Manila wall time.
public class RadiologyStudy
{
    public const int AccessionLength = 64, NameLength = 200, ShortTextLength = 2000, ReportTextLength = 4000, FindingsLength = 8000;

    public int Id { get; set; }
    public int PatientRecordId { get; set; }
    public PatientRecord Patient { get; set; } = null!;
    public int? ClinicalEncounterId { get; set; }
    public ClinicalEncounter? Encounter { get; set; }

    public string AccessionNumber { get; set; } = string.Empty;   // e.g. "DRMC-RAD-2026-0101"
    public string StudyName { get; set; } = string.Empty;         // e.g. "Chest X-ray, PA view"
    public RadiologyModality Modality { get; set; } = RadiologyModality.XRay;
    public string BodyRegion { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public RadiologyStatus Status { get; set; } = RadiologyStatus.InProgress;

    public string OrderingPhysician { get; set; } = string.Empty; // text snapshot
    public string RadiologistName { get; set; } = string.Empty;   // text snapshot
    public string PerformingUnit { get; set; } = string.Empty;

    public string ClinicalIndication { get; set; } = string.Empty;
    public string Technique { get; set; } = string.Empty;
    public string Comparison { get; set; } = string.Empty;
    public string Findings { get; set; } = string.Empty;
    public string Impression { get; set; } = string.Empty;
    public string PlainLanguageSummary { get; set; } = string.Empty;
    public string AmendmentNote { get; set; } = string.Empty;
    public string InternalNotes { get; set; } = string.Empty;     // staff-only; never rendered to patients

    public bool IsReported => Status is RadiologyStatus.Final or RadiologyStatus.Amended;
}

public enum RadiologyModality
{
    [Display(Name = "X-ray")] XRay,
    [Display(Name = "Fluoroscopy")] Fluoroscopy,
    [Display(Name = "Ultrasound")] Ultrasound,
    [Display(Name = "CT scan")] CT,
    [Display(Name = "MRI")] MRI,
    [Display(Name = "Mammography")] Mammography,
    [Display(Name = "Other")] Other
}

public enum RadiologyStatus
{
    [Display(Name = "In progress")] InProgress,
    [Display(Name = "Pending verification")] PendingVerification,
    [Display(Name = "Final")] Final,
    [Display(Name = "Amended")] Amended
}
