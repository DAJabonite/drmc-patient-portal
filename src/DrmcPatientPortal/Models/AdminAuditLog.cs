namespace DrmcPatientPortal.Models;

public class AdminAuditLog
{
    public long Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorEmail { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string RecordKey { get; set; } = string.Empty;
    public int? SubjectPatientId { get; set; }
    public string ChangedFields { get; set; } = "[]";
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}
