namespace DrmcPatientPortal.Models;

// Display-only seeded row for the "Next Appointment" dashboard card.
public class NextAppointment
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string Department { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = string.Empty; // e.g., Confirmed
    public string ProviderName { get; set; } = string.Empty;
}
