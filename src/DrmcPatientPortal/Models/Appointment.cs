namespace DrmcPatientPortal.Models;

public class Appointment
{
    public int Id { get; set; }
    public string BookingReference { get; set; } = string.Empty; // e.g. "DRMC-2026-IM-8492"
    
    // Nullable for guest/self-service booking before registration, populated for logged-in accounts
    public string? PatientUserId { get; set; }
    public ApplicationUser? Patient { get; set; }

    public string PatientName { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhilHealthNumber { get; set; }

    public string Department { get; set; } = string.Empty;
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public string DoctorName { get; set; } = string.Empty;

    // Computed property for backwards compatibility with previous dashboard card
    public string ProviderName => string.IsNullOrWhiteSpace(DoctorName) ? "Attending OPD Physician" : DoctorName;

    public string Type { get; set; } = "In-Person OPD"; // "In-Person OPD", "Teleconsultation"
    public DateTime ScheduledAt { get; set; }
    public string TimeSlot { get; set; } = string.Empty; // e.g. "09:00 AM - 09:30 AM"
    public string ChiefComplaint { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed"; // "Confirmed", "Pending", "Completed", "Cancelled"

    public string QrCodePayload { get; set; } = string.Empty;
    public string? PublicAccessTokenHash { get; set; }
    public DateTime? PublicAccessExpiresAt { get; set; }
    public string? TeleconsultMeetingUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
