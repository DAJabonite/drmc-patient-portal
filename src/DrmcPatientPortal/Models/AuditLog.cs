namespace DrmcPatientPortal.Models;

// Access transparency and security events are stored in the application database.
// Production deployments must add an approved retention policy, sensitive-detail masking,
// and tamper-resistant export to immutable storage or a remote security monitoring system.
public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty; // e.g. "VIEW_LAB_REPORT" or "VIEW_PRESCRIPTION"
    public string Resource { get; set; } = string.Empty; // Target entity, e.g., "LabResult/1", "Prescription/2"
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
