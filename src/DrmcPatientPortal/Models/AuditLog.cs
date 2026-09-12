namespace DrmcPatientPortal.Models;

// SECURITY REVIEW TODO (Item #3): PHI Access Audit Logging & Tamper Resistance
// BOUNDARY NOTE: Access transparency and security events (e.g. viewing records and submitting intake)
// are persisted to the database.
// Production hardening requirements:
// 1. Immutable / WORM or remote SIEM log forwarding (e.g. Syslog/Kafka/Azure Sentinel) to prevent tampering.
// 2. Automated log retention policy matching DOH guidelines (10 years).
// 3. Masking of sensitive query parameters in Details string.
// Reference: docs/SECURITY_REVIEW_TODO.md
public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty; // "VIEW_LAB_REPORT", "SUBMIT_TRIAGE_INTAKE", "REQUEST_REFILL"
    public string Resource { get; set; } = string.Empty; // Target entity, e.g., "LabResult/1", "Prescription/2"
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
