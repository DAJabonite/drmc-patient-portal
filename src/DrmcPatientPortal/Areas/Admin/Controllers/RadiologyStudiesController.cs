using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using DrmcPatientPortal.Areas.Admin.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[RequestSizeLimit(LabReportStorage.MaximumBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = LabReportStorage.MaximumBytes + 1024 * 1024)]
public sealed class RadiologyStudiesController(ApplicationDbContext db, AdminWrites writes,
    IRadiologyReportStorage storage, IAuditLogService audit, TimeProvider clock, ILogger<RadiologyStudiesController> logger) :
    AdminCrudController<RadiologyStudy, RadiologyInput>(db, writes, new RadiologyEditor())
{
    private string? previousReport;

    protected override async Task<WriteResult> SaveRecordAsync(ApplicationDbContext database, RadiologyInput input, RadiologyStudy entity, CancellationToken token)
    {
        // Obtain the generated ID within the same transaction before binding PDF encryption to it.
        await database.SaveChangesAsync(token);
        if (input.Report is null) return WriteResult.Success;
        StoredLabReport stored;
        try { stored = await storage.StoreAsync(entity.Id, input.Report, token); }
        catch (InvalidDataException error) { return WriteResult.Invalid(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return WriteResult.Invalid("The PDF could not be stored. Choose the file again and try saving."); }
        previousReport = entity.ReportFileName;
        ReportAttachments.Apply(entity, stored, clock.GetUtcNow().UtcDateTime);
        await database.SaveChangesAsync(token);
        return WriteResult.Success;
    }

    protected override void RecordSaved()
    {
        // Keep the old attachment until both the metadata and required audit have committed.
        if (previousReport is null) return;
        try { storage.Delete(previousReport); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.LogWarning("An unreferenced radiology report could not be removed after saving."); }
    }

    [HttpGet, Authorize(Policy = AdminPolicies.Radiology)]
    public async Task<IActionResult> Report(int id, CancellationToken token)
    {
        var study = await Database.RadiologyStudies.SingleOrDefaultAsync(s => s.Id == id, token);
        if (study is null) return NotFound();
        await AdminAudit.ViewAsync(Database, User, "RadiologyStudies", [study.PatientRecordId], "Report", token);
        return View(new RadiologyReportPage(study.Id, study.StudyName, study.AccessionNumber, study.ReportFileName is not null,
            study.ReportSize, study.ReportUploadedAtUtc, Convert.ToBase64String(Database.Entry(study).Property<byte[]>("RowVersion").CurrentValue!),
            ClinicalClock.WallTime(clock.GetUtcNow()), study.Status));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPolicies.Radiology)]
    public async Task<IActionResult> ReleaseReport(int id, string? rowVersion, DateTime? releasedAt,
        RadiologyStatus status, string? amendmentNote, CancellationToken token)
    {
        var version = AdminAudit.Version(rowVersion);
        var now = ClinicalClock.WallTime(clock.GetUtcNow());
        if (!ModelState.IsValid || version is null || releasedAt is null || releasedAt.Value.Kind != DateTimeKind.Unspecified
            || releasedAt > now || status is not (RadiologyStatus.Final or RadiologyStatus.Amended)
            || (amendmentNote?.Length ?? 0) > RadiologyStudy.ShortTextLength
            || status == RadiologyStatus.Amended && string.IsNullOrWhiteSpace(amendmentNote))
        {
            TempData["ReportError"] = "Reload the report and provide a valid release time and status. Amended reports need an amendment note.";
            return RedirectToAction(nameof(Report), new { id });
        }
        var result = await Writes.RunAsync(async database =>
        {
            var study = await database.RadiologyStudies.SingleOrDefaultAsync(s => s.Id == id, token);
            if (study is null || !version.SequenceEqual(database.Entry(study).Property<byte[]>("RowVersion").CurrentValue!)) return WriteResult.Stale;
            if (study.ReportFileName is null) return WriteResult.Invalid("An attached PDF is required for PDF release.");
            if (releasedAt < study.PerformedAt) return WriteResult.Invalid("Release cannot precede the exam.");
            // Verify the current attachment can actually be opened before publishing it.
            try { await storage.ReadAsync(study.Id, study.ReportFileName, token); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { return WriteResult.Invalid("The attached PDF is unavailable. Try again after storage is restored."); }
            study.ReleasedAt = releasedAt;
            study.Status = status;
            if (status == RadiologyStatus.Amended) study.AmendmentNote = amendmentNote!.Trim();
            database.Entry(study).Property("RowVersion").OriginalValue = version;
            await AdminAudit.AddAsync(database, User, "Release PDF", "RadiologyStudies", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                study.PatientRecordId, fields: [nameof(study.Status), nameof(study.ReleasedAt), nameof(study.AmendmentNote)], cancellationToken: token);
            return WriteResult.Success;
        }, token);
        TempData[result.Succeeded ? "ReportSuccess" : "ReportError"] = result.Succeeded ? "Radiology report released." : result.Error;
        if (result.Conflict) return StatusCode(409);
        return RedirectToAction(nameof(Report), new { id });
    }

    [HttpGet, Authorize(Policy = AdminPolicies.Radiology)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PreviewReport(int id, bool download, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var study = await Database.RadiologyStudies.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, token);
        if (study?.ReportFileName is null) return NotFound();
        byte[] bytes;
        try { bytes = await storage.ReadAsync(id, study.ReportFileName, token); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        try
        {
            await AdminAudit.ViewAsync(Database, User, "RadiologyStudies", [study.PatientRecordId], download ? "Download PDF" : "Preview PDF", token);
            await audit.LogAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), download ? "DOWNLOAD_RADIOLOGY_PDF" : "VIEW_RADIOLOGY_PDF",
                $"RadiologyStudy/{id}", "Staff accessed radiology PDF.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        }
        catch (AuditLogPersistenceException) { return StatusCode(503); }
        var current = await Database.RadiologyStudies.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, token);
        if (current?.ReportFileName != study.ReportFileName || current.PatientRecordId != study.PatientRecordId) return NotFound();
        return new PrivateFileResult(bytes, "application/pdf", $"radiology-report-{id}.pdf", download);
    }
}
