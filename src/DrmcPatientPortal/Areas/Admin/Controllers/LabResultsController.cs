using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DrmcPatientPortal.Areas.Admin.Security;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[RequestSizeLimit(LabReportStorage.MaximumBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = LabReportStorage.MaximumBytes + 1024 * 1024)]
public sealed class LabResultsController(ApplicationDbContext db, AdminWrites writes, ILabReportStorage storage,
    ILogger<LabResultsController> logger, IAuditLogService auditLog) : AdminCrudController<LabResult, LabUploadInput>(db, writes, new LabUploadEditor())
{
    private string? previousReport;

    [HttpGet]
    public async Task<IActionResult> NeedsEncounterAssignment(int page = 1, CancellationToken token = default)
    {
        var query = Database.LabResults.Include(l => l.Patient).Where(l => l.RequiresEncounterAssignment);
        var count = await query.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
        var rows = await query.OrderBy(l => l.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        await AdminAudit.ViewAsync(Database, User, "LabResults", rows.Select(l => l.PatientRecordId), "Needs encounter assignment", token);
        return View("/Areas/Admin/Views/Shared/EncounterAssignmentQueue.cshtml", new AdminList("Needs encounter assignment",
            rows.Select(l => new AdminRow(l.Id, l.TestName, l.Patient.FullName + " | Hospital number: " + l.Patient.HospitalNumber + " | " +
                (l.CollectedAt.HasValue ? l.CollectedAt.Value.ToString("yyyy-MM-dd") : "Test date not recorded"), l.PatientRecordId)).ToArray(), null, page, count));
    }

    [HttpGet]
    public async Task<IActionResult> AssignEncounter(int id, CancellationToken token)
    {
        var source = await Database.LabResults.Include(l => l.Patient).SingleOrDefaultAsync(l => l.Id == id && l.RequiresEncounterAssignment, token);
        if (source is null) return NotFound();
        await AdminAudit.ViewAsync(Database, User, "LabResults", [source.PatientRecordId], "Assign encounter", token);
        ViewData["Encounters"] = await EncounterLinks.OptionsAsync(Database, source.PatientRecordId, token);
        var pending = await Database.LabResults.Include(l => l.Encounter).Where(l => l.PatientRecordId == source.PatientRecordId
            && l.ClinicalEncounterId != null && !l.RequiresEncounterAssignment && l.ReportFileName == null && l.Status != "Cancelled")
            .OrderByDescending(l => l.Id).ToListAsync(token);
        var choices = pending.Select(l => new PendingRequestChoice(PendingRequestKeys.Create(l.Id, Database.Entry(l).Property<byte[]>("RowVersion").CurrentValue!),
            $"{l.Encounter!.EncounterReference} · {l.AccessionNumber} · {l.TestName}", l.ClinicalEncounterId!.Value)).ToArray();
        return View("/Areas/Admin/Views/Shared/AssignEncounter.cshtml", new ResultAssignmentPage(id, source.TestName, source.Patient.FullName,
            source.Patient.HospitalNumber ?? "", source.CollectedAt, new ResultAssignmentInput { RowVersion = Convert.ToBase64String(Database.Entry(source).Property<byte[]>("RowVersion").CurrentValue!) }, choices));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignEncounter(int id, ResultAssignmentInput input, CancellationToken token)
    {
        if (ModelState.IsValid)
        {
            var result = await Writes.RunAsync(async database =>
            {
                try { return await ResultAssignments.LaboratoryAsync(database, storage, id, input, User, token); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                { return WriteResult.Invalid("The encrypted report could not be read or stored. Restore storage access and retry."); }
            }, token);
            if (result.Succeeded) return RedirectToAction(nameof(NeedsEncounterAssignment));
            if (result.Conflict) Response.StatusCode = 409;
            ModelState.AddModelError("", result.Error!);
        }
        return await AssignEncounter(id, token);
    }


    [HttpGet]
    public async Task<IActionResult> BatchUpload(int id = 0, string? search = null, int page = 1, CancellationToken token = default)
    {
        if (id == 0)
        {
            if (search?.Length > 450) return BadRequest();
            var choices = await Editor.SelectContextAsync(Database, search, page, token);
            await AdminAudit.ViewAsync(Database, User, "LabResults", choices.Rows.Where(r => r.PatientId.HasValue).Select(r => r.PatientId!.Value), "Batch upload context", token);
            ViewData["ContextAction"] = nameof(BatchUpload);
            return View("/Areas/Admin/Views/Shared/SelectContext.cshtml", choices);
        }
        return await BatchForm(id, new LabBatchInput(), token);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(26 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    public async Task<IActionResult> BatchUpload(int id, LabBatchInput input, CancellationToken token)
    {
        if (input.Files.Count is < 1 or > 10 || input.Files.Sum(f => f.Length) > 25L * 1024 * 1024)
            ModelState.AddModelError("", "Choose 1–10 PDF files, at most 10 MiB each and 25 MiB combined.");
        if (input.Items.Count != input.Files.Count) ModelState.AddModelError("", "Provide a description or an existing request for each selected PDF.");
        if (input.CollectedAt is { Kind: not DateTimeKind.Unspecified }) ModelState.AddModelError(nameof(input.CollectedAt), "Enter Manila wall time without a time-zone suffix.");
        var keys = input.Items.Where(i => !string.IsNullOrWhiteSpace(i.ExistingRequest)).Select(i => PendingRequestKeys.Parse(i.ExistingRequest)).ToArray();
        if (keys.Any(k => k is null) || keys.Select(k => k!.Value.Id).Distinct().Count() != keys.Length)
            ModelState.AddModelError("", "Choose each existing request at most once and reload stale choices.");
        if (input.Items.Any(i => string.IsNullOrWhiteSpace(i.ExistingRequest) && string.IsNullOrWhiteSpace(i.Description)))
            ModelState.AddModelError("", "Enter an exam description for every new result.");
        if (ModelState.IsValid)
        {
            try { foreach (var file in input.Files) await PdfReportValidation.ValidateAsync(file, token); }
            catch (InvalidDataException error) { ModelState.AddModelError("", error.Message); }
        }
        if (!ModelState.IsValid) return await BatchForm(id, input, token);
        var result = await Writes.RunAsync(async database =>
        {
            var patient = await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == id, token);
            if (patient is null) return WriteResult.Invalid("The patient record no longer exists.");
            if (!patient.HasHospitalNumber) return WriteResult.Invalid(LabUploadEditor.MissingHospitalNumberError);
            if (!await EncounterLinks.ValidAsync(database, input.ClinicalEncounterId, id, true, token))
                return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
            var entities = new List<(LabResult Entity, byte[]? Version, bool New)>();
            foreach (var item in input.Items)
            {
                if (PendingRequestKeys.Parse(item.ExistingRequest) is { } key)
                {
                    var request = await database.LabResults.SingleOrDefaultAsync(l => l.Id == key.Id, token);
                    if (request is null || !key.Version.SequenceEqual(AdminEntity<LabResult, LabUploadInput>.Version(database, request))) return WriteResult.Stale;
                    if (request.PatientRecordId != id || request.ClinicalEncounterId != input.ClinicalEncounterId || request.ReportFileName is not null
                        || request.RequiresEncounterAssignment || request.Status == "Cancelled")
                        return WriteResult.Invalid("The selected pending request is not an unattached request for this patient's encounter.");
                    if (request.CollectedAt.HasValue && request.CollectedAt.Value.Date != input.CollectedAt!.Value.Date)
                        return WriteResult.Invalid("The test date differs from the selected request. Correct the request explicitly or upload it in a separate batch.");
                    entities.Add((request, key.Version, false));
                }
                else entities.Add((new LabResult { PatientRecordId = id, ClinicalEncounterId = input.ClinicalEncounterId,
                    AccessionNumber = "PORTAL-LAB-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    TestName = item.Description!.Trim(), Category = LabCategory.SpecialDiagnostics, PerformingUnit = "" }, null, true));
            }
            // Every target and file has been checked before the first insert or ciphertext write.
            for (var index = 0; index < entities.Count; index++)
            {
                var (entity, version, isNew) = entities[index];
                if (isNew) database.LabResults.Add(entity);
                else database.Entry(entity).Property("RowVersion").OriginalValue = version!;
                entity.CollectedAt ??= input.CollectedAt!.Value.Date;
                entity.Status = "Pending Verification"; entity.ReleasedAt = null; entity.PatientActivityAtUtc = DateTime.UtcNow;
                await database.SaveChangesAsync(token);
                try { ReportAttachments.Apply(entity, await storage.StoreAsync(entity.Id, input.Files[index], token), DateTime.UtcNow); }
                catch (InvalidDataException error) { return WriteResult.Invalid(error.Message); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { return WriteResult.Invalid("The PDFs could not be stored. No batch records were committed. Choose the files again and retry."); }
                await AdminAudit.AddAsync(database, User, isNew ? "Batch create" : "Fulfill request", "LabResults", entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    id, fields: [nameof(LabResult.ReportFileName), nameof(LabResult.ClinicalEncounterId), nameof(LabResult.CollectedAt), nameof(LabResult.Status)], cancellationToken: token);
            }
            return WriteResult.Success;
        }, token);
        if (result.Succeeded)
        {
            TempData["Success"] = $"Uploaded {input.Files.Count} PDF reports for the selected encounter. Review them before release.";
            return RedirectToAction(nameof(Index), new { contextId = id });
        }
        if (result.Conflict) Response.StatusCode = 409;
        ModelState.AddModelError("", result.Error!);
        return await BatchForm(id, input, token);
    }

    private async Task<IActionResult> BatchForm(int id, LabBatchInput input, CancellationToken token)
    {
        var context = await Editor.CreateContextAsync(Database, id, token);
        if (context is null) return NotFound();
        await AdminAudit.ViewAsync(Database, User, "LabResults", [id], "Batch upload", token);
        ViewData["Context"] = context;
        ViewData["Encounters"] = await EncounterLinks.OptionsAsync(Database, id, token);
        ViewData["Requests"] = await PendingChoices(id, token);
        return View("BatchUpload", input);
    }

    private async Task<IReadOnlyList<PendingRequestChoice>> PendingChoices(int patientId, CancellationToken token)
    {
        var requests = await Database.LabResults.Include(l => l.Encounter)
            .Where(l => l.PatientRecordId == patientId && l.ClinicalEncounterId != null && !l.RequiresEncounterAssignment
                && l.ReportFileName == null && l.Status != "Cancelled").OrderByDescending(l => l.Id).ToListAsync(token);
        return requests.Select(l => new PendingRequestChoice(PendingRequestKeys.Create(l.Id, Database.Entry(l).Property<byte[]>("RowVersion").CurrentValue!),
            $"{l.Encounter!.EncounterReference} · {l.AccessionNumber} · {l.TestName} · {(l.CollectedAt.HasValue ? l.CollectedAt.Value.ToString("yyyy-MM-dd") : "Test date not recorded")}", l.ClinicalEncounterId!.Value)).ToArray();
    }

    protected override async Task<WriteResult> SaveRecordAsync(ApplicationDbContext database, LabUploadInput input, LabResult entity, CancellationToken token)
    {
        // Creation needs the generated lab ID before storage can bind encryption to the record.
        // Both saves and the required audit remain in the existing transaction.
        await database.SaveChangesAsync(token);
        if (input.Report is null) return WriteResult.Success;
        StoredLabReport stored;
        try { stored = await storage.StoreAsync(entity.Id, input.Report, token); }
        catch (InvalidDataException error) { return WriteResult.Invalid(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return WriteResult.Invalid("The PDF could not be stored. Choose the file again and try saving."); }
        previousReport = entity.ReportFileName;
        ReportAttachments.Apply(entity, stored, DateTime.UtcNow);
        await database.SaveChangesAsync(token);
        return WriteResult.Success;
    }

    protected override void RecordSaved()
    {
        // Keep the old PDF until the new record metadata and audit have committed.
        if (previousReport is null) return;
        try { storage.Delete(previousReport); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.LogWarning("An unreferenced lab report could not be removed after saving."); }
    }

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
        var current = await Database.LabResults.Include(l => l.Patient).SingleOrDefaultAsync(l => l.Id == id, token);
        if (current is null) return NotFound();
        if (!current.Patient.HasHospitalNumber)
        {
            TempData["ReportError"] = LabUploadEditor.MissingHospitalNumberError;
            return RedirectToAction(nameof(Report), new { area = "Admin", id });
        }
        if (current.RequiresEncounterAssignment || current.CollectedAt is null || !await EncounterLinks.ValidAsync(Database, current.ClinicalEncounterId, current.PatientRecordId, true, token))
        {
            TempData["ReportError"] = "Assign the originating encounter and actual test date before uploading a PDF.";
            return RedirectToAction(nameof(Report), new { area = "Admin", id });
        }
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
            var lab = await database.LabResults.Include(l => l.Patient).SingleOrDefaultAsync(l => l.Id == id, token);
            if (lab is null || !version.SequenceEqual(AdminEntity<LabResult, LabUploadInput>.Version(database, lab))) return WriteResult.Stale;
            if (stored is not null && !lab.Patient.HasHospitalNumber)
                return WriteResult.Invalid(LabUploadEditor.MissingHospitalNumberError);
            if (stored is not null && (lab.RequiresEncounterAssignment || lab.CollectedAt is null || !await EncounterLinks.ValidAsync(database, lab.ClinicalEncounterId, lab.PatientRecordId, true, token)))
                return WriteResult.Invalid("Assign the originating encounter and actual test date before uploading a PDF.");
            previous = lab.ReportFileName;
            ReportAttachments.Apply(lab, stored, DateTime.UtcNow);
            lab.Status = stored is null ? "In progress" : "Pending Verification";
            lab.ReleasedAt = null; lab.PatientActivityAtUtc = DateTime.UtcNow;
            database.Entry(lab).Property("RowVersion").OriginalValue = version;
            await database.SaveChangesAsync(token);
            await AdminAudit.AddAsync(database, User, stored is null ? "Remove report" : "Upload report", "LabResults",
                id.ToString(System.Globalization.CultureInfo.InvariantCulture), lab.PatientRecordId,
                fields: [nameof(LabResult.ReportFileName), nameof(LabResult.ReportSize), nameof(LabResult.ReportUploadedAtUtc),
                    nameof(LabResult.ReportOriginalFileNameProtected), nameof(LabResult.ReportContentType), nameof(LabResult.ReportSha256)], cancellationToken: token);
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
    [Authorize(Policy = AdminPolicies.Lab)]
    public async Task<IActionResult> PreviewReport(int id, bool download, CancellationToken token)
    {
        var lab = await Database.LabResults.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, token);
        if (lab?.ReportFileName is null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        byte[] bytes;
        try { bytes = await storage.ReadAsync(id, lab.ReportFileName, token); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        try
        {
            await AdminAudit.ViewAsync(Database, User, "LabResults", [lab.PatientRecordId], download ? "Download PDF" : "Preview PDF", token);
            await auditLog.LogAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), download ? "DOWNLOAD_LAB_PDF" : "VIEW_LAB_PDF",
                $"LabResult/{id}", "Staff accessed laboratory report PDF.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        }
        catch (AuditLogPersistenceException) { return StatusCode(503); }
        return new PrivateFileResult(bytes, "application/pdf", $"lab-report-{id}.pdf", download);
    }
}
