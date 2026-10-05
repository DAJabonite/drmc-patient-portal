using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed record AuditPage(IReadOnlyList<AdminAuditLog> Rows, string? Search, int? PatientId, int Page, int Total)
{
    public int Pages => Math.Max(1, (Total + 24) / 25);
}
