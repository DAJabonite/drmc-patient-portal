namespace DrmcPatientPortal.Models;

public class RefillRequest
{
    public int Id { get; set; }
    public int PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;
    public string PatientUserId { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public RefillStatus Status { get; set; } = RefillStatus.Requested; // Requested, Approved, ReadyForPickup, Dispensed, Rejected
    public string? PharmacyNotes { get; set; }
    public DateTime? EstimatedPickupDate { get; set; }
}

public enum RefillStatus
{
    Requested,
    Approved,
    ReadyForPickup,
    Dispensed,
    Rejected
}
