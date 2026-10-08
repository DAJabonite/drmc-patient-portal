using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class LabResultsController(ApplicationDbContext db, AdminWrites writes, ILabReportStorage storage,
    ILogger<LabResultsController> logger) : AdminCrudController<LabResult, LabInput>(db, writes, new LabEditor())
{
    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken token)
    {
        var lab = await Database.LabResults.SingleOrDefaultAsync(l => l.Id == id, token);
        if (lab is null) return NotFound();
        await AdminAudit.ViewAsync(Database, User, "LabResults", [lab.PatientRecordId], "Report", token);
        return View(new LabReportPage(lab.Id, lab.TestName, lab.AccessionNumber, lab.ReportFileName is not null,
            lab.ReportSize, lab.ReportUploadedAtUtc, Convert.ToBase64String(Database.Entry(lab).Property<byte[]>("RowVersion").CurrentValue!)));
    }

    [HttpPost, RequestSizeLimit(LabReportStorage.MaximumBytes + 65536),
        RequestFormLimits(MultipartBodyLengthLimit = LabReportStorage.MaximumBytes + 65536)]
    public async Task<IActionResult> UploadReport(int id, string? rowVersion, IFormFile? report, CancellationToken token)
    {
        if (report is null || AdminAudit.Version(rowVersion) is null)
        {
            TempData["ReportError"] = "Choose a PDF and reload the record before saving.";
            return RedirectToAction(nameof(Report), new { area = "Admin", id });
        }
        if (!await Database.LabResults.AnyAsync(l => l.Id == id, token)) return NotFound();
        StoredLabReport stored;
        try { stored = await storage.StoreAsync(id, report, token); }
        catch (InvalidDataException error)
        {
            TempData["ReportError"] = error.Message;
            return RedirectToAction(nameof(Report), new { area = "Admin", id });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            TempData["ReportError"] = "The PDF could not be stored. Please try again.";
            return RedirectToAction(nameof(Report), new { area = "Admin", id });
        }
        return await SaveReport(id, rowVersion, stored, token);
    }

    [HttpPost]
    public Task<IActionResult> RemoveReport(int id, string? rowVersion, CancellationToken token) =>
        SaveReport(id, rowVersion, null, token);

    private async Task<IActionResult> SaveReport(int id, string? rowVersion, StoredLabReport? stored, CancellationToken token)
    {
        var version = AdminAudit.Version(rowVersion);
        string? previous = null;
        var result = version is null ? WriteResult.Stale : await Writes.RunAsync(async database =>
        {
            var lab = await database.LabResults.SingleOrDefaultAsync(l => l.Id == id, token);
            if (lab is null || !version.SequenceEqual(AdminEntity<LabResult, LabInput>.Version(database, lab))) return WriteResult.Stale;
            previous = lab.ReportFileName;
            lab.ReportFileName = stored?.FileName;
            lab.ReportSize = stored?.Size;
            lab.ReportUploadedAtUtc = stored is null ? null : DateTime.UtcNow;
            database.Entry(lab).Property("RowVersion").OriginalValue = version;
            await database.SaveChangesAsync(token);
            await AdminAudit.AddAsync(database, User, stored is null ? "Remove report" : "Upload report", "LabResults",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture), lab.PatientRecordId,
                fields: [nameof(LabResult.ReportFileName), nameof(LabResult.ReportSize), nameof(LabResult.ReportUploadedAtUtc)], cancellationToken: token);
            return WriteResult.Success;
        }, token);
        // A failed/uncertain commit may have persisted the new reference. Retain both files in that
        // case; orphan cleanup must confirm database references before deleting anything.
        if (result.Succeeded && previous is not null)
        {
            try { storage.Delete(previous); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("An unreferenced lab report could not be removed for record {LabId}.", id);
            }
        }
        TempData[result.Succeeded ? "ReportSuccess" : "ReportError"] = result.Succeeded ? "Lab report updated." : result.Error;
        return RedirectToAction(nameof(Report), new { area = "Admin", id });
    }

    [HttpGet]
    public async Task<IActionResult> PreviewReport(int id, bool download, CancellationToken token)
    {
        var lab = await Database.LabResults.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, token);
        if (lab?.ReportFileName is null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        byte[] bytes;
        try { bytes = await storage.ReadAsync(id, lab.ReportFileName, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        await AdminAudit.ViewAsync(Database, User, "LabResults", [lab.PatientRecordId], download ? "Download PDF" : "Preview PDF", token);
        return download ? File(bytes, "application/pdf", $"lab-report-{id}.pdf") : File(bytes, "application/pdf");
    }
}
