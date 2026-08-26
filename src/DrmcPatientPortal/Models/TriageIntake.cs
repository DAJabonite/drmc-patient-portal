namespace DrmcPatientPortal.Models;

// PRE-CONSULTATION SELF-TRIAGE & DIGITAL INTAKE
// BOUNDARY NOTE: Clinical intake responses and acuity assessments are recorded in the database.
// Real-time synchronization to attending clinician workstations is an infrastructure boundary (logged to ILogger).
// A clinician-side dashboard is out-of-scope for this patient-only portal build.
public class TriageIntake
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string ChiefComplaint { get; set; } = string.Empty;
    public int SymptomDurationDays { get; set; }
    public int PainScale { get; set; } // 0 to 10 visual analog scale

    // Structured Symptom Flags (JSON array of strings)
    public string SymptomsJson { get; set; } = "[]";
    public bool HasEmergencyRedFlags { get; set; } // Severe chest pain, acute dyspnea, sudden numbness, high-grade persistent fever

    // Self-Reported Vitals
    public string? ReportedBloodPressure { get; set; }
    public string? ReportedTemperature { get; set; }
    public string? ReportedHeartRate { get; set; }
    public string? ReportedWeightKg { get; set; }
    public string? ReportedBloodSugar { get; set; }

    public string ComorbiditiesJson { get; set; } = "[]"; // JSON array: ["Hypertension", "Diabetes", "Asthma"]
    public string CurrentMedicationsSummary { get; set; } = string.Empty;
    public TriageAcuity AcuityLevel { get; set; } = TriageAcuity.Routine; // Routine, Priority, UrgentEmergency
    public string TriageNotes { get; set; } = string.Empty;
}

public enum TriageAcuity
{
    Routine,
    Priority,
    UrgentEmergency
}
