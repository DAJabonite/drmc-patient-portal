using System.Security.Claims;

namespace DrmcPatientPortal.Services;

// Reasons and resources are server-controlled. Never include submitted credentials or demographics.
public static class HospitalRecordLinkAudit
{
    public const string FailureAction = "LINK_HOSPITAL_RECORD_FAILED";

    public static Task RejectedAsync(IAuditLogService audit, HttpContext context, string reason,
        string resource = "PatientRegistrationCode") =>
        audit.LogAsync(context.User.FindFirstValue(ClaimTypes.NameIdentifier), FailureAction, resource, reason,
            context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
}
