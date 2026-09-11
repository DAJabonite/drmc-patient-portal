using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Models;

[Index(nameof(RegistrationReference), IsUnique = true)]
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string IdType { get; set; } = string.Empty;
    public string IdNumber { get; set; } = string.Empty;

    // Retained during migration so existing records and older code remain readable.
    // New registration code must use the separate notice and optional-consent fields below.
    [Obsolete("Use PrivacyNoticeAcknowledgedAtUtc and OptionalConsentGranted.")]
    public bool PrivacyConsent { get; set; } = true;

    public AccountLifecycleStatus AccountStatus { get; internal set; } = AccountLifecycleStatus.Active;

    [MaxLength(64)]
    public string? RegistrationReference { get; internal set; }

    public DateTime AccountStatusChangedAtUtc { get; internal set; } = DateTime.UtcNow;

    [MaxLength(300)]
    public string? AccountStatusReason { get; internal set; }

    public DateTime? PrivacyNoticeAcknowledgedAtUtc { get; internal set; }

    [MaxLength(40)]
    public string? PrivacyNoticeVersion { get; internal set; }

    public bool OptionalConsentGranted { get; internal set; }

    [MaxLength(120)]
    public string? OptionalConsentPurpose { get; internal set; }

    public DateTime? OptionalConsentRecordedAtUtc { get; internal set; }

    public bool CanAccessProtectedPatientData => AccountStatus == AccountLifecycleStatus.Active;

    public string? PreferredDepartment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? Sex { get; set; }
    public string? BloodType { get; set; }

    public ICollection<PatientIdDocument> IdDocuments { get; set; } = new List<PatientIdDocument>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<QueueTicket> QueueTickets { get; set; } = new List<QueueTicket>();
    public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
    public ICollection<TriageIntake> TriageIntakes { get; set; } = new List<TriageIntake>();
    public ICollection<MessageThread> MessageThreads { get; set; } = new List<MessageThread>();
    public ICollection<DependentProfile> DependentProfiles { get; set; } = new List<DependentProfile>();
    public ICollection<ConsentLogEntry> ConsentLogEntries { get; set; } = new List<ConsentLogEntry>();
}
