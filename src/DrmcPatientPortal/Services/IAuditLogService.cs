namespace DrmcPatientPortal.Services;

public interface IAuditLogService
{
    Task LogAsync(string? userId, string action, string resource, string details, string ipAddress);
}
