using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

// BOUNDARY NOTE: Access transparency and security events (e.g. viewing records and submitting intake)
// are persisted before the protected operation succeeds. External immutable SIEM retention remains a DRMC deployment dependency.
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
            throw new AuditLogPersistenceException("The required security audit record could not be persisted.", ex);
        }
    }
}
