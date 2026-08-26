using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

// SECURITY REVIEW TODO (Item #3): PHI Access Audit Logging & Tamper Resistance
// BOUNDARY NOTE: Access transparency and security events (e.g. proxy switching, viewing lab reports, intake submissions)
// are persisted to the database and mirrored to ILogger.
// Production hardening requirements:
// 1. Immutable / WORM or remote SIEM log forwarding (e.g. Syslog/Kafka/Azure Sentinel) to prevent tampering.
// 2. Automated log retention policy matching DOH guidelines (10 years).
// 3. Masking of sensitive query parameters in Details string.
// Reference: docs/SECURITY_REVIEW_TODO.md
public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ApplicationDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string? userId, string action, string resource, string details, string ipAddress)
    {
        try
        {
            var log = new AuditLog
            {
                UserId = userId,
                Action = action,
                Resource = resource,
                Details = details,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();

            _logger.LogInformation("[AUDIT] User={UserId} Action={Action} Resource={Resource} IP={IpAddress}", userId ?? "Anonymous", action, resource, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist audit log entry: {Action} on {Resource}", action, resource);
        }
    }
}
