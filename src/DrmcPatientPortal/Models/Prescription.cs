namespace DrmcPatientPortal.Models;

public class Prescription
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string RxNumber { get; set; } = string.Empty; // e.g. "DRMC-RX-2026-4421"
    public string GenericName { get; set; } = string.Empty; // e.g. "Metformin Hydrochloride"
    public string? BrandName { get; set; }                  // e.g. "Glucophage"
    public string Dosage { get; set; } = string.Empty;     // e.g. "500 mg"
    public string DosageForm { get; set; } = string.Empty; // Tablet, Capsule, Syrup, Inhaler
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

    public ICollection<RefillRequest> RefillRequests { get; set; } = new List<RefillRequest>();
}

public enum PrescriptionStatus
{
    Active,
    Completed,
    Discontinued,
    Expired
}
