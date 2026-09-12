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

    // Optional: patient can store a preferred department for reference.
    public string? PreferredDepartment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Demographics extracted from ID or provided manually (all nullable/optional)
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? Sex { get; set; }
    public string? BloodType { get; set; }

    // Navigation collections
    public ICollection<PatientIdDocument> IdDocuments { get; set; } = new List<PatientIdDocument>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<QueueTicket> QueueTickets { get; set; } = new List<QueueTicket>();
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
    public ICollection<TriageIntake> TriageIntakes { get; set; } = new List<TriageIntake>();
}
