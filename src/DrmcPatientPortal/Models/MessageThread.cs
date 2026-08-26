namespace DrmcPatientPortal.Models;

// SECURITY REVIEW TODO (Item #1): Asynchronous Care Communication & PHI Protection
// BOUNDARY NOTE: Real threaded messaging between patients and clinical departments is fully operational in-portal.
// Before deployment with real patient health information (PHI), a security engineer must review:
// 1. Column/Field-level encryption-at-rest (AES-256) for Message.Body and Subject lines per NPC/DOH Data Privacy regulations.
// 2. Access control authorization policies ensuring only licensed healthcare staff assigned to the department can view threads.
// 3. Automated rate limiting to prevent staff inbox exhaustion.
// Reference: docs/SECURITY_REVIEW_TODO.md
public class MessageThread
{
    public int Id { get; set; }
    public string PatientUserId { get; set; } = string.Empty;
    public ApplicationUser Patient { get; set; } = null!;

    public string Department { get; set; } = string.Empty; // e.g. "Internal Medicine", "OPD Pharmacy", "Pediatrics"
    public string Subject { get; set; } = string.Empty;
    public MessageCategory Category { get; set; } = MessageCategory.GeneralInquiry;
    public ThreadStatus Status { get; set; } = ThreadStatus.Open; // Open, InProgress, Resolved, Closed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public enum MessageCategory
{
    AppointmentInquiry,
    LabResultClarification,
    MedicationQuestion,
    CareGuidance,
    GeneralInquiry
}

public enum ThreadStatus
{
    Open,
    InProgress,
    Resolved,
    Closed
}
