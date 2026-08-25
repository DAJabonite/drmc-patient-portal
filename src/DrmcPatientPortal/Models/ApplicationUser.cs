using Microsoft.AspNetCore.Identity;

namespace DrmcPatientPortal.Models;

// Extended Identity user carrying real patient profile fields.
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string ContactNumber { get; set; } = string.Empty;

    public string IdType { get; set; } = string.Empty;

    public string IdNumber { get; set; } = string.Empty;

    public bool PrivacyConsent { get; set; } = true;

    // Optional: patient can store a preferred department for reference (not a booking).
    public string? PreferredDepartment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation collections for seeded dashboard data (kept small, display-only).
    public ICollection<NextAppointment> Appointments { get; set; } = new List<NextAppointment>();
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
