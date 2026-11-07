namespace DrmcPatientPortal.Models;

public class Prescription
{
    public int Id { get; set; }
    public int PatientRecordId { get; set; }
    public PatientRecord Patient { get; set; } = null!;
    public int? ClinicalEncounterId { get; set; }
    public ClinicalEncounter? Encounter { get; set; }
    public DateTime? PatientActivityAtUtc { get; set; }

    public string RxNumber { get; set; } = string.Empty; // e.g. "DRMC-RX-2026-4421"
    public string GenericName { get; set; } = string.Empty; // e.g. "Metformin Hydrochloride"
    public string? BrandName { get; set; }                  // e.g. "Glucophage"
    public string Dosage { get; set; } = string.Empty;     // e.g. "500 mg"
    public string DosageForm { get; set; } = string.Empty; // Tablet, Capsule, Syrup, Inhaler
    public string? RouteOfAdministration { get; set; }
    public string Frequency { get; set; } = string.Empty;  // "Twice daily with meals (8:00 AM, 6:00 PM)"
    public string Instructions { get; set; } = string.Empty; // "Take with or immediately after meals."

    public string PrescribingDoctor { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime PrescribedAt { get; set; }
    public DateTime ValidUntil { get; set; }
    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Active;

    public int RefillsTotal { get; set; } = 3;
    public int RefillsRemaining { get; set; } = 2;
    public DateTime? LastRefillDate { get; set; }

    public PrescriptionFulfillmentStatus FulfillmentStatus { get; set; }
    public PrescriptionFrequencyMode? FrequencyMode { get; set; }
    public decimal? FrequencyValue { get; set; }
    public decimal? DosePerIntake { get; set; }
    public string? DoseUnit { get; set; }
    public int? DurationCount { get; set; }
    public PrescriptionDurationUnit? DurationUnit { get; set; }
    public decimal? PrescribedQuantity { get; set; }
    public decimal? DispensedQuantity { get; set; }
    public DateTime? ServedDate { get; set; }
    public DateTime? NextRefillDate { get; set; }
    public bool IsRedStar { get; set; }
    public int? RedStarEnrollmentId { get; set; }
    public RedStarEnrollment? RedStarEnrollment { get; set; }

    public ICollection<MedicationDoseSchedule> DoseSchedules { get; set; } = new List<MedicationDoseSchedule>();
}

public enum PrescriptionFulfillmentStatus { Prescribed, Served }
public enum PrescriptionFrequencyMode { TimesPerDay, EveryHours, AsNeeded }
public enum PrescriptionDurationUnit { Days, Months }

public enum PrescriptionStatus
{
    Active,
    Completed,
    Discontinued,
    Expired
}
