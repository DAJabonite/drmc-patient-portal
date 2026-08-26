namespace DrmcPatientPortal.Models;

public class ClinicalEncounter
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string EncounterReference { get; set; } = string.Empty; // e.g. "DRMC-ENC-2026-1104"
    public DateTime EncounterDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string AttendingPhysician { get; set; } = string.Empty;
    public EncounterType Type { get; set; } = EncounterType.OpdConsultation; // OpdConsultation, Teleconsultation, Emergency, Inpatient

    public string ChiefComplaint { get; set; } = string.Empty;
    public string PrimaryDiagnosis { get; set; } = string.Empty; // e.g. "Essential Hypertension (ICD-10 I10)"
    public string? SecondaryDiagnosis { get; set; }             // e.g. "Type 2 Diabetes Mellitus without complications (ICD-10 E11.9)"
    public string ClinicalSummary { get; set; } = string.Empty;
    public string CarePlanAndInstructions { get; set; } = string.Empty;

    public string VitalSignsRecorded { get; set; } = string.Empty; // "BP: 120/80 mmHg | HR: 74 bpm | Temp: 36.6 C | Wt: 64 kg"
    public DateTime? FollowUpDate { get; set; }
    public string FollowUpNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum EncounterType
{
    OpdConsultation,
    Teleconsultation,
    Emergency,
    Inpatient
}
