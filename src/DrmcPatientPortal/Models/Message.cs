namespace DrmcPatientPortal.Models;

// SECURITY REVIEW TODO (Item #1): Asynchronous Care Communication & Message Storage
// BOUNDARY NOTE: Message bodies are stored in SQLite for functional workflow testing.
// Security review required before production PHI ingestion (encryption-at-rest & audit trails).
// Reference: docs/SECURITY_REVIEW_TODO.md
public class Message
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public MessageThread Thread { get; set; } = null!;

    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public MessageSenderRole SenderRole { get; set; } = MessageSenderRole.Patient; // Patient, CareTeam, Doctor

    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}

public enum MessageSenderRole
{
    Patient,
    CareTeam,
    Doctor
}
