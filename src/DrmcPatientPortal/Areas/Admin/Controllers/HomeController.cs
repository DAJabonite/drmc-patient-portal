using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController(ApplicationDbContext db) : Controller
{
    private static readonly ImportStatus[] OpenImportStatuses =
    [
        ImportStatus.Staged, ImportStatus.Validated, ImportStatus.Queued, ImportStatus.Running,
        ImportStatus.ValidationQueued, ImportStatus.Validating, ImportStatus.ApprovalQueued, ImportStatus.Approving,
    ];

    // Aggregate counts only; the dashboard never lists patient names or clinical values.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>
        {
            ["Patients"] = await db.PatientRecords.CountAsync(cancellationToken),
            ["ClinicalEncounters"] = await db.ClinicalEncounters.CountAsync(cancellationToken),
            ["LabResults"] = await db.LabResults.CountAsync(cancellationToken),
            ["LabResultItems"] = await db.LabResultItems.CountAsync(cancellationToken),
            ["Prescriptions"] = await db.Prescriptions.CountAsync(cancellationToken),
            ["MedicationDoseSchedules"] = await db.MedicationDoseSchedules.CountAsync(cancellationToken),
            ["PatientAllergies"] = await db.PatientAllergies.CountAsync(cancellationToken),
            ["Doctors"] = await db.Doctors.CountAsync(cancellationToken),
            ["PublicAdvisories"] = await db.PublicAdvisories.CountAsync(cancellationToken),
            ["Imports"] = await db.ImportBatches.CountAsync(cancellationToken),
            ["Audit"] = await db.AdminAuditLogs.CountAsync(cancellationToken),
        };
        var labsAwaiting = await db.LabResults.CountAsync(l => l.Status != "Available", cancellationToken);
        var openImports = await db.ImportBatches.CountAsync(b => OpenImportStatuses.Contains(b.Status), cancellationToken);
        var unlinked = await db.PatientRecords.CountAsync(p => p.PortalUserId == null, cancellationToken);
        var activePrescriptions = await db.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Active, cancellationToken);

        AdminMetric[] attention =
        [
            new("Labs awaiting release", labsAwaiting, "bi-hourglass-split", "LabResults", "In progress or pending verification", labsAwaiting > 0),
            new("Imports in progress", openImports, "bi-arrow-repeat", "Imports", "Staged, validating or awaiting approval", openImports > 0),
            new("Unlinked patient records", unlinked, "bi-link-45deg", "Patients", "No verified portal account yet", unlinked > 0),
            new("Active prescriptions", activePrescriptions, "bi-capsule", "Prescriptions", "Visible on patient medication lists"),
        ];

        var recentAudit = await db.AdminAuditLogs.AsNoTracking().OrderByDescending(a => a.TimestampUtc).Take(6).ToListAsync(cancellationToken);
        var recentImports = await db.ImportBatches.AsNoTracking().OrderByDescending(b => b.CreatedAtUtc).Take(5).ToListAsync(cancellationToken);
        return View(new AdminDashboard(attention, counts, recentAudit, recentImports));
    }
}
