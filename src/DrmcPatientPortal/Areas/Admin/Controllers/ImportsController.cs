using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class ImportsController(ApplicationDbContext db, ImportStaging staging, ImportWorkflow workflow) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        var total = await db.ImportBatches.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var rows = await db.ImportBatches.AsNoTracking().OrderByDescending(batch => batch.CreatedAtUtc).ThenBy(batch => batch.Id).Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken);
        ViewData["ImportStatuses"] = await ImportStatusDisplay.LoadAsync(rows, staging, cancellationToken);
        return View(new ImportIndex(rows, page, total));
    }

    [HttpGet]
    public IActionResult Template(string name)
    {
        if (name == "Workbook_v1") return File(ImportParser.ExampleWorkbook(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Drmc_v1.xlsx");
        try { var template = ImportTemplates.Get(name); return File(ImportExamples.Csv(template), "text/csv; charset=utf-8", template.Sheet + ".csv"); }
        catch (ImportRejectedException) { return BadRequest(); }
    }

    [HttpPost, RequestSizeLimit(ImportParser.MaxBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = ImportParser.MaxBytes)]
    public async Task<IActionResult> Upload(ImportUpload input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || input.File is null) { TempData["ImportMessage"] = "Select a file and its template."; return RedirectToAction(nameof(Index)); }
        var id = Guid.NewGuid(); var saved = false;
        try
        {
            if (input.File.Length <= 0 || input.File.Length > ImportParser.MaxBytes) throw new ImportRejectedException("Upload a file of at most 20 MB.");
            var format = Path.GetExtension(input.File.FileName).ToLowerInvariant() switch { ".csv" => "csv", ".xlsx" => "xlsx", _ => throw new ImportRejectedException("Only UTF-8 CSV and .xlsx files are accepted.") };
            using var bytes = new MemoryStream();
            await using var stream = input.File.OpenReadStream();
            var buffer = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            { if (bytes.Length + read > ImportParser.MaxBytes) throw new ImportRejectedException("The upload exceeds 20 MB."); await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
            var content = bytes.ToArray(); var rows = ImportParser.Parse(content, format, input.Template);
            var actor = await db.Users.AsNoTracking().SingleAsync(user => user.Id == User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
            await staging.SaveAsync(id, new ImportEnvelope { Content = content, Format = format, Template = input.Template }, cancellationToken);
            db.ImportBatches.Add(new ImportBatch { Id = id, ActorId = actor.Id, ActorEmail = actor.Email ?? "", Template = input.Template, FileHash = ImportStaging.FileHash(content), CreatedAtUtc = DateTime.UtcNow, RowCount = rows.Count, Status = ImportStatus.Staged });
            await db.SaveChangesAsync(cancellationToken); saved = true;
            return RedirectToAction(nameof(Reconcile), new { id });
        }
        catch (ImportRejectedException error) { TempData["ImportMessage"] = error.Message; return RedirectToAction(nameof(Index)); }
        catch (Exception error) when (error is DbUpdateException or IOException or UnauthorizedAccessException) { TempData["ImportMessage"] = "The upload could not be staged. Please try again."; return RedirectToAction(nameof(Index)); }
        finally
        {
            if (!saved)
                try { staging.Purge(id); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [HttpGet]
    public async Task<IActionResult> Reconcile([FromRoute] Guid id, int page = 1, string? patientSearch = null, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        if (batch.Status is not (ImportStatus.Staged or ImportStatus.Validated) || batch.CreatedAtUtc <= DateTime.UtcNow.AddDays(-7)) return RedirectToAction(nameof(Details), new { id });
        try
        {
            var envelope = await staging.LoadAsync(id, cancellationToken);
            var groups = ImportValidation.Groups(ImportParser.Parse(envelope.Content, envelope.Format, envelope.Template));
            page = Math.Clamp(page, 1, Math.Max(1, (groups.Count + 24) / 25));
            var visible = groups.Skip((page - 1) * 25).Take(25).ToArray();
            var mappings = visible.Select(group => envelope.Mappings.SingleOrDefault(mapping => mapping.SourcePatientKey == group.Key) ?? new ImportMapping
            { SourcePatientKey = group.Key, Patient = new PatientInput { FullName = group.Name, HospitalNumber = group.HospitalNumber, DateOfBirth = DateTime.TryParseExact(group.BirthDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null } }).ToList();
            if (groups.Count == 0)
            {
                await PreviewAudit(id, [], cancellationToken);
                ViewData["Patients"] = Array.Empty<SelectListItem>();
                return View(new ImportReconciliation(batch, visible, new ImportDryRun { RowVersion = Convert.ToBase64String(batch.RowVersion) }, page, 0));
            }
            var query = db.PatientRecords.AsNoTracking().AsQueryable();
            if (patientSearch?.Length > 450) return BadRequest();
            if (!string.IsNullOrWhiteSpace(patientSearch)) query = query.Where(patient => EF.Functions.Collate(patient.FullName, "Latin1_General_100_CI_AS").Contains(patientSearch) || patient.HospitalNumber != null && EF.Functions.Collate(patient.HospitalNumber, "Latin1_General_100_CI_AS").Contains(patientSearch));
            var picker = await query.OrderBy(patient => patient.Id).Take(25).ToListAsync(cancellationToken);
            var selectedIds = mappings.Where(mapping => mapping.PatientRecordId.HasValue).Select(mapping => mapping.PatientRecordId!.Value).ToArray();
            picker.AddRange(await db.PatientRecords.AsNoTracking().Where(patient => selectedIds.Contains(patient.Id)).ToListAsync(cancellationToken));
            await PreviewAudit(id, picker.Select(patient => patient.Id), cancellationToken);
            ViewData["Patients"] = picker.DistinctBy(patient => patient.Id).OrderBy(patient => patient.Id).Select(patient => new SelectListItem("#" + patient.Id + " | " + patient.FullName + " | " + (patient.HospitalNumber ?? "unassigned") + " | " + patient.DateOfBirth?.ToString("yyyy-MM-dd"), patient.Id.ToString())).ToArray();
            ViewData["PatientSearch"] = patientSearch;
            return View(new ImportReconciliation(batch, visible, new ImportDryRun { RowVersion = Convert.ToBase64String(batch.RowVersion), Mappings = mappings }, page, groups.Count));
        }
        catch (ImportRejectedException error) { TempData["ImportMessage"] = error.Message; return RedirectToAction(nameof(Details), new { id }); }
    }

    [HttpPost]
    public async Task<IActionResult> DryRun([FromRoute] Guid id, ImportDryRun input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { TempData["ImportMessage"] = "Review the setup fields."; return RedirectToAction(nameof(Reconcile), new { id }); }
        return await Command(id, () => workflow.DryRunAsync(id, input, cancellationToken, User));
    }

    [HttpPost]
    public async Task<IActionResult> Approve([FromRoute] Guid id, ImportApproval input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { TempData["ImportMessage"] = "Confirm the reviewed records and proposed counts before approval."; return RedirectToAction(nameof(Details), new { id }); }
        return await Command(id, () => workflow.ApproveAsync(id, input, User, cancellationToken));
    }

    [HttpPost]
    public Task<IActionResult> Cancel([FromRoute] Guid id, string rowVersion, CancellationToken cancellationToken) => Command(id, () => workflow.CancelAsync(id, rowVersion, cancellationToken));

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        var display = await ImportStatusDisplay.LoadAsync(batch, staging, cancellationToken);
        return Json(new { status = batch.Status.ToString(), batch.RowCount, batch.CreatedCount, rowVersion = Convert.ToBase64String(batch.RowVersion),
            statusLabel = display.Text, statusCss = display.Css, pending = ImportStatusDisplay.Pending(batch.Status), canCancel = ImportStatusDisplay.CanCancel(batch.Status) });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] Guid id, int page = 1, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        ImportReport? report = null;
        var verified = false;
        if (!batch.StagingPurged && batch.Status is not (ImportStatus.Succeeded or ImportStatus.Cancelled or ImportStatus.Expired or ImportStatus.ValidationQueued or ImportStatus.Validating))
        {
            try
            {
                var envelope = await staging.LoadAsync(id, cancellationToken);
                report = envelope.Report;
                verified = ImportStatusDisplay.VerifiedReport(batch, envelope);
                if (report is not null)
                {
                    page = Math.Clamp(page, 1, Math.Max(1, (report.Rows.Count + 24) / 25));
                    var keys = report.Rows.Skip((page - 1) * 25).Take(25).Select(row => row.PatientKey.Trim()).ToHashSet(StringComparer.Ordinal);
                    await PreviewAudit(id, envelope.Mappings.Where(mapping => keys.Contains(mapping.SourcePatientKey) && mapping.PatientRecordId.HasValue).Select(mapping => mapping.PatientRecordId!.Value), cancellationToken);
                }
            }
            catch (ImportRejectedException error) { TempData["ImportMessage"] = error.Message; }
        }
        return View(new ImportDetail(batch, report, page, verified));
    }

    private async Task<IActionResult> Command(Guid id, Func<Task<WriteResult>> operation)
    {
        try { var result = await operation(); if (!result.Succeeded) TempData["ImportMessage"] = result.Error; }
        catch (ImportRejectedException error) { TempData["ImportMessage"] = error.Message; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { TempData["ImportMessage"] = "Import storage is unavailable. Refresh the batch status before trying again."; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PreviewAudit(Guid id, IEnumerable<int> patients, CancellationToken token)
    {
        try
        {
            await AdminAudit.AddAsync(db, User, "View", "ImportBatches", id.ToString(), fields: [], cancellationToken: token);
            await AdminAudit.ViewAsync(db, User, "ImportBatches", patients, id.ToString(), token);
        }
        catch (Exception error) when (error is not OperationCanceledException) { throw new AuditLogPersistenceException("Required import preview logging failed.", error); }
    }
}
