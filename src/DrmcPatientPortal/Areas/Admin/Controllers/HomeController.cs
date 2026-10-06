using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController(ApplicationDbContext db, AdminAccessScope scope) : Controller
{
    private static readonly ImportStatus[] OpenImportStatuses =
    [
        ImportStatus.Staged, ImportStatus.Validated, ImportStatus.Queued, ImportStatus.Running,
        ImportStatus.ValidationQueued, ImportStatus.Validating, ImportStatus.ApprovalQueued, ImportStatus.Approving,
    ];

    // Aggregate counts only; the dashboard never lists patient names or clinical values. Staff see
    // only counts, metrics, audit entries and import metadata for modules their roles can open.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var roles = await scope.RolesAsync();
        bool Can(string controller) => AdminPolicies.Allows(controller, roles);
        var counters = new (string Controller, Func<Task<int>> Count)[]
        {
            ("Patients", () => db.PatientRecords.CountAsync(cancellationToken)),
            ("RegistrationCodes", () => db.PatientRegistrationCodes.CountAsync(cancellationToken)),
            ("ClinicalEncounters", () => db.ClinicalEncounters.CountAsync(cancellationToken)),
            ("LabResults", () => db.LabResults.CountAsync(cancellationToken)),
            ("LabResultItems", () => db.LabResultItems.CountAsync(cancellationToken)),
            ("RadiologyStudies", () => db.RadiologyStudies.CountAsync(cancellationToken)),
            ("Prescriptions", () => db.Prescriptions.CountAsync(cancellationToken)),
            ("MedicationDoseSchedules", () => db.MedicationDoseSchedules.CountAsync(cancellationToken)),
            ("PatientAllergies", () => db.PatientAllergies.CountAsync(cancellationToken)),
            ("Doctors", () => db.Doctors.CountAsync(cancellationToken)),
            ("PublicAdvisories", () => db.PublicAdvisories.CountAsync(cancellationToken)),
            ("Imports", () => db.ImportBatches.CountAsync(cancellationToken)),
            ("Audit", () => db.AdminAuditLogs.CountAsync(cancellationToken)),
        };
        var counts = new Dictionary<string, int>();
        foreach (var (controller, count) in counters)
            if (Can(controller)) counts[controller] = await count();

        var now = DrmcPatientPortal.Services.ClinicalClock.WallTime(DateTimeOffset.UtcNow);
        var utcNow = DateTime.UtcNow;
        var metrics = new (string Controller, Func<Task<AdminMetric>> Build)[]
        {
            ("LabResults", async () => { var n = await db.LabResults.CountAsync(l => l.Status != "Available", cancellationToken);
                return new AdminMetric("Labs awaiting release", n, "bi-hourglass-split", "LabResults", "In progress or pending verification", n > 0); }),
            ("RadiologyStudies", async () => { var n = await db.RadiologyStudies.CountAsync(r =>
                    r.Status != RadiologyStatus.Final && r.Status != RadiologyStatus.Amended || r.ReleasedAt == null || r.ReleasedAt > now, cancellationToken);
                return new AdminMetric("Radiology awaiting release", n, "bi-hourglass-split", "RadiologyStudies", "Not final, or release time not reached", n > 0); }),
            ("Imports", async () => { var n = await db.ImportBatches.CountAsync(b => OpenImportStatuses.Contains(b.Status), cancellationToken);
                return new AdminMetric("Imports in progress", n, "bi-arrow-repeat", "Imports", "Staged, validating or awaiting approval", n > 0); }),
            ("Patients", async () => { var n = await db.PatientRecords.CountAsync(p => p.PortalUserId == null, cancellationToken);
                return new AdminMetric("Unlinked patient records", n, "bi-link-45deg", "Patients", "No verified portal account yet", n > 0); }),
            ("RegistrationCodes", async () => { var n = await db.PatientRegistrationCodes.CountAsync(c =>
                    c.RedeemedAtUtc == null && c.RevokedAtUtc == null && c.ExpiresAtUtc > utcNow, cancellationToken);
                return new AdminMetric("Active registration codes", n, "bi-qr-code", "RegistrationCodes", "Issued and waiting for patient signup"); }),
            ("Prescriptions", async () => { var n = await db.Prescriptions.CountAsync(p => p.Status == PrescriptionStatus.Active, cancellationToken);
                return new AdminMetric("Active prescriptions", n, "bi-capsule", "Prescriptions", "Visible on patient medication lists"); }),
        };
        var attention = new List<AdminMetric>();
        foreach (var (controller, build) in metrics)
            if (Can(controller)) attention.Add(await build());

        var recentAudit = Can("Audit")
            ? await db.AdminAuditLogs.AsNoTracking().OrderByDescending(a => a.TimestampUtc).Take(6).ToListAsync(cancellationToken)
            : [];
        var recentImports = Can("Imports")
            ? await db.ImportBatches.AsNoTracking().OrderByDescending(b => b.CreatedAtUtc).Take(5).ToListAsync(cancellationToken)
            : [];
        return View(new AdminDashboard(AdminNavigation.For(roles), attention, counts, recentAudit, recentImports));
    }
}
