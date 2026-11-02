using System.Security.Claims;
using System.Text.Json;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = AdminPolicies.RedStar)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RedStarLabReportsController(ApplicationDbContext db, AdminWrites writes,
    RedStarReportAccess access, IRedStarLabReportStorage storage) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("A clinic actor is required.");

    [HttpGet]
    public async Task<IActionResult> Index(int id, int page = 1, CancellationToken token = default)
    {
        if (id == 0)
        {
            var count = await db.RedStarPatients.CountAsync(token);
            page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
            var patients = await db.RedStarPatients.AsNoTracking().OrderBy(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
            await RedStarAuditTrail.RecordAsync(db, Actor, "Choose private report patient", "Registry", token);
            return View("Patients", new RedStarClinicPickerPage(patients, page, count));
        }
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        if (clinic is null) return NotFound();
        var hospital = await access.MappedPatientAsync(id, token);
        var query = db.RedStarLabReports.AsNoTracking().Include(r => r.Encounter).Where(r => r.RedStarPatientId == id);
        var total = await query.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var reports = await query.OrderByDescending(r => r.PatientActivityAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * 25).Take(25).ToListAsync(token);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View private lab reports", $"Patient/{id}", token);
        return View(new RedStarLabPage(clinic, hospital, reports, page, total));
    }

    [HttpGet]
    public async Task<IActionResult> Mapping(int id, CancellationToken token)
    {
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        if (clinic is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, Actor, "View hospital mapping", $"Patient/{id}", token);
        return View(new RedStarMappingPage(clinic, new() { HospitalNumber = clinic.HospitalNumber ?? "",
            PatientVersion = Convert.ToBase64String(clinic.RowVersion) }, null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(int id, RedStarHospitalMappingInput input, CancellationToken token)
    {
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        if (clinic is null) return NotFound();
        PatientRecord? hospital = null;
        if (ModelState.IsValid)
        {
            if (!MatchesVersion(clinic.RowVersion, input.PatientVersion)) return Conflict("The clinic record changed. Reload the form.");
            hospital = await MatchHospitalAsync(db, clinic, input, token);
            if (hospital is null) ModelState.AddModelError("", "The clinic and hospital identity could not be verified. Check the owning records before trying again.");
        }
        if (hospital is not null)
        {
            input.HospitalPatientRecordId = hospital.Id;
            input.HospitalVersion = Convert.ToBase64String(hospital.RowVersion);
            await RedStarAuditTrail.RecordAsync(db, Actor, "Review hospital mapping", $"Patient/{id}", token);
        }
        return View("Mapping", new RedStarMappingPage(clinic, input, hospital));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Map(int id, RedStarHospitalMappingInput input, CancellationToken token)
    {
        if (!input.IdentityVerified) ModelState.AddModelError("", "Confirm that you verified the clinic identity against the hospital record and clinic source record or ID.");
        if (!ModelState.IsValid) return await MappingError(id, input, token);
        var result = await writes.RunAsync(async database =>
        {
            var clinic = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == id, token);
            if (clinic is null || !MatchesVersion(clinic.RowVersion, input.PatientVersion)) return WriteResult.Stale;
            var hospital = await MatchHospitalAsync(database, clinic, input, token);
            if (hospital is null || hospital.Id != input.HospitalPatientRecordId || !MatchesVersion(hospital.RowVersion, input.HospitalVersion)) return WriteResult.Stale;
            if (await database.RedStarPatients.AnyAsync(p => p.Id != id && p.HospitalPatientRecordId == hospital.Id, token))
                return WriteResult.Invalid("That hospital record is already mapped to another private registry patient. Reconcile the clinic record first.");
            var previous = clinic.HospitalPatientRecordId;
            clinic.HospitalPatientRecordId = hospital.Id;
            clinic.HospitalMappingVerifiedAtUtc = DateTime.UtcNow;
            clinic.HospitalMappingFingerprint = RedStarReportAccess.Fingerprint(clinic, hospital);
            database.Entry(clinic).Property(p => p.RowVersion).OriginalValue = AdminAudit.Version(input.PatientVersion)!;
            await RedStarAuditTrail.RecordAsync(database, Actor, "Verify hospital mapping", $"Patient/{id}", token,
                JsonSerializer.Serialize(new { PreviousHospitalRecord = previous, HospitalRecord = hospital.Id, IdentityAttested = true }));
            return WriteResult.Success;
        }, token);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { id });
        AddError(result);
        return await MappingError(id, input, token);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unmap(int id, string? patientVersion, bool identityVerified, CancellationToken token)
    {
        if (!identityVerified) return BadRequest();
        var result = await writes.RunAsync(async database =>
        {
            var clinic = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == id, token);
            if (clinic is null || !MatchesVersion(clinic.RowVersion, patientVersion)) return WriteResult.Stale;
            var previous = clinic.HospitalPatientRecordId;
            clinic.HospitalPatientRecordId = null; clinic.HospitalMappingVerifiedAtUtc = null; clinic.HospitalMappingFingerprint = null;
            database.Entry(clinic).Property(p => p.RowVersion).OriginalValue = AdminAudit.Version(patientVersion)!;
            await RedStarAuditTrail.RecordAsync(database, Actor, "Remove hospital mapping", $"Patient/{id}", token,
                JsonSerializer.Serialize(new { PreviousHospitalRecord = previous }));
            return WriteResult.Success;
        }, token);
        if (!result.Succeeded) TempData["RedStarError"] = result.Error;
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Upload(int id, CancellationToken token) => await UploadForm(id, new(), token);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(int id, RedStarLabUploadInput input, CancellationToken token)
    {
        var rows = input.Rows.Where(r => r.Report is not null || !string.IsNullOrWhiteSpace(r.Description) || r.PendingRequestId.HasValue).ToArray();
        input.Rows = rows.Length == 0 ? [new()] : rows.ToList();
        if (rows.Length is < 1 or > 10) ModelState.AddModelError("", "Complete between one and ten request or PDF rows.");
        if (rows.Where(r => r.Report is not null).Sum(r => r.Report!.Length) > 25L * 1024 * 1024) ModelState.AddModelError("", "The combined PDFs must be no larger than 25 MiB.");
        if (rows.Where(r => r.PendingRequestId.HasValue).GroupBy(r => r.PendingRequestId).Any(g => g.Count() > 1)) ModelState.AddModelError("", "Select a pending request only once in a batch.");
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Description) && !row.PendingRequestId.HasValue) ModelState.AddModelError("", "Describe each new request, for example CD4 or Viral Load.");
            if (row.PendingRequestId.HasValue && row.Report is null) ModelState.AddModelError("", "Attach a PDF to fulfill a pending request.");
            if (row.Report is not null)
            {
                if (!input.CollectedAt.HasValue) ModelState.AddModelError("", "Enter the actual test date for uploaded results.");
                try { await PdfReportValidation.ValidateAsync(row.Report, token); }
                catch (InvalidDataException error) { ModelState.AddModelError("", error.Message); }
            }
        }
        foreach (var row in rows)
        {
            var effectiveStatus = row.Report is null ? "In progress" : input.Status == "In progress" ? "Pending Verification" : input.Status;
            ValidateRelease(effectiveStatus, input.CollectedAt, row.Report is null ? null : input.ReleasedAt, row.Report is not null);
        }
        if (!ModelState.IsValid) return await UploadForm(id, input, token);
        var result = await writes.RunAsync(async database =>
        {
            var clinic = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == id, token);
            if (clinic is null || !MatchesVersion(clinic.RowVersion, input.PatientVersion)) return WriteResult.Stale;
            var hospital = clinic.HospitalPatientRecordId.HasValue ? await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == clinic.HospitalPatientRecordId.Value, token) : null;
            if (hospital is null || !RedStarReportAccess.MappingValid(clinic, hospital)) return WriteResult.Invalid("Verify the hospital mapping before adding reports.");
            if (!await database.ClinicalEncounters.AnyAsync(e => e.Id == input.ClinicalEncounterId && e.PatientRecordId == hospital.Id, token))
                return WriteResult.Invalid("Select an encounter belonging to the mapped hospital patient.");
            var resolved = new List<(RedStarLabUploadRow Input, RedStarLabReport Report)>();
            foreach (var row in rows)
            {
                RedStarLabReport report;
                if (row.PendingRequestId is int requestId)
                {
                    var pending = await database.RedStarLabReports.SingleOrDefaultAsync(r => r.Id == requestId && r.RedStarPatientId == id && r.PatientRecordId == hospital.Id && r.ClinicalEncounterId == input.ClinicalEncounterId, token);
                    if (pending is null || !MatchesVersion(pending.RowVersion, row.RowVersion)) return WriteResult.Stale;
                    if (pending.ReportFileName is not null || pending.Status != "In progress") return WriteResult.Invalid("Only an unattached pending request can be fulfilled.");
                    if (!string.IsNullOrWhiteSpace(row.Description) && row.Description.Trim() != pending.Description || pending.CollectedAt.HasValue && pending.CollectedAt.Value.Date != input.CollectedAt?.Date)
                        return WriteResult.Invalid("The request description or recorded test date differs. Correct the request explicitly before uploading.");
                    database.Entry(pending).Property(r => r.RowVersion).OriginalValue = AdminAudit.Version(row.RowVersion)!;
                    report = pending;
                }
                else report = new() { RedStarPatientId = id, PatientRecordId = hospital.Id, ClinicalEncounterId = input.ClinicalEncounterId, Description = row.Description!.Trim() };
                resolved.Add((row, report));
            }
            foreach (var (row, report) in resolved)
            {
                report.CollectedAt = input.CollectedAt?.Date;
                report.Status = row.Report is null ? "In progress" : input.Status == "In progress" ? "Pending Verification" : input.Status;
                report.ReleasedAt = row.Report is null ? null : input.ReleasedAt;
                report.PatientActivityAtUtc = DateTime.UtcNow;
                if (report.Id == 0) database.RedStarLabReports.Add(report);
                await database.SaveChangesAsync(token);
                if (row.Report is not null) RedStarReportAccess.Attach(report, await storage.StoreAsync(report.Id, row.Report, token));
                await RedStarAuditTrail.RecordAsync(database, Actor, "Save private lab request", $"LabReport/{report.Id}", token);
            }
            return WriteResult.Success;
        }, token);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { id });
        AddError(result);
        return await UploadForm(id, input, token);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken token)
    {
        var report = await db.RedStarLabReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
        if (report is null) return NotFound();
        return await EditForm(report, new() { RowVersion = Convert.ToBase64String(report.RowVersion), Description = report.Description,
            ClinicalEncounterId = report.ClinicalEncounterId, CollectedAt = report.CollectedAt, Status = report.Status, ReleasedAt = report.ReleasedAt }, token);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RedStarLabEditInput input, CancellationToken token)
    {
        var original = await db.RedStarLabReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
        if (original is null) return NotFound();
        if (input.Report is not null)
        {
            try { await PdfReportValidation.ValidateAsync(input.Report, token); }
            catch (InvalidDataException error) { ModelState.AddModelError("", error.Message); }
        }
        if (input.RemoveReport && input.Report is not null) ModelState.AddModelError("", "Choose either replacement or removal.");
        var hasPdf = input.Report is not null || !input.RemoveReport && original.ReportFileName is not null;
        if (hasPdf && input.Status == "In progress") input.Status = "Pending Verification";
        ValidateRelease(input.Status, input.CollectedAt, input.ReleasedAt, hasPdf);
        if (!ModelState.IsValid) return await EditForm(original, input, token);
        var result = await writes.RunAsync(async database =>
        {
            var report = await database.RedStarLabReports.Include(r => r.RedStarPatient).SingleOrDefaultAsync(r => r.Id == id, token);
            if (report is null || !MatchesVersion(report.RowVersion, input.RowVersion)) return WriteResult.Stale;
            var clinic = report.RedStarPatient;
            var hospital = clinic.HospitalPatientRecordId.HasValue ? await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == clinic.HospitalPatientRecordId.Value, token) : null;
            if (hospital is null || !RedStarReportAccess.MappingValid(clinic, hospital)) return WriteResult.Invalid("Verify the hospital mapping before editing reports.");
            if (!await database.ClinicalEncounters.AnyAsync(e => e.Id == input.ClinicalEncounterId && e.PatientRecordId == hospital.Id, token)) return WriteResult.Invalid("Select an encounter for the mapped patient.");
            if ((report.PatientRecordId != hospital.Id || report.ClinicalEncounterId != input.ClinicalEncounterId || input.Report is not null && report.ReportFileName is not null || input.RemoveReport) && string.IsNullOrWhiteSpace(input.CorrectionReason))
                return WriteResult.Invalid("Record a reason for reassignment, replacement or removal. Existing reports are never transferred automatically.");
            database.Entry(report).Property(r => r.RowVersion).OriginalValue = AdminAudit.Version(input.RowVersion)!;
            var before = new { report.PatientRecordId, report.ClinicalEncounterId, report.Status };
            report.PatientRecordId = hospital.Id; report.ClinicalEncounterId = input.ClinicalEncounterId;
            report.Description = input.Description.Trim(); report.CollectedAt = input.CollectedAt?.Date;
            report.Status = input.Status; report.ReleasedAt = input.ReleasedAt; report.PatientActivityAtUtc = DateTime.UtcNow;
            if (input.Report is not null) RedStarReportAccess.Attach(report, await storage.StoreAsync(id, input.Report, token));
            else if (input.RemoveReport) RedStarReportAccess.Attach(report, null);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Correct private lab report", $"LabReport/{id}", token,
                JsonSerializer.Serialize(new { Before = before, report.PatientRecordId, report.ClinicalEncounterId, report.Status, input.CorrectionReason }));
            return WriteResult.Success;
        }, token);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { id = original.RedStarPatientId });
        AddError(result); return await EditForm(original, input, token);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, string? rowVersion, string? reason, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) return BadRequest();
        var clinicId = 0;
        var result = await writes.RunAsync(async database =>
        {
            var report = await database.RedStarLabReports.SingleOrDefaultAsync(r => r.Id == id, token);
            if (report is null || !MatchesVersion(report.RowVersion, rowVersion)) return WriteResult.Stale;
            clinicId = report.RedStarPatientId; report.Status = "Withdrawn"; report.ReleasedAt = null;
            report.PatientActivityAtUtc = DateTime.UtcNow;
            database.Entry(report).Property(r => r.RowVersion).OriginalValue = AdminAudit.Version(rowVersion)!;
            await RedStarAuditTrail.RecordAsync(database, Actor, "Withdraw private lab report", $"LabReport/{id}", token, reason.Trim());
            return WriteResult.Success;
        }, token);
        if (!result.Succeeded) return Conflict(result.Error);
        return RedirectToAction(nameof(Index), new { id = clinicId });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, bool download = false, CancellationToken token = default)
    {
        var report = await db.RedStarLabReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, token);
        if (report?.ReportFileName is null) return NotFound();
        try
        {
            var bytes = await storage.ReadAsync(id, report.ReportFileName, token);
            await RedStarAuditTrail.RecordAsync(db, Actor, download ? "Download private PDF" : "Preview private PDF", $"LabReport/{id}", token);
            return new PrivateFileResult(bytes, "application/pdf", $"clinic-report-{id}.pdf", download);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or AuditLogPersistenceException) { return StatusCode(503); }
    }

    private static async Task<PatientRecord?> MatchHospitalAsync(ApplicationDbContext database, RedStarPatient clinic, RedStarHospitalMappingInput input, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(clinic.HospitalNumber) || !string.Equals(clinic.HospitalNumber.Trim(), input.HospitalNumber.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
        var hospitalNumber = input.HospitalNumber.Trim();
        var candidates = await database.PatientRecords.Where(p => p.HospitalNumber != null && EF.Functions.Collate(p.HospitalNumber, "Latin1_General_100_CI_AS") == hospitalNumber).Take(2).ToListAsync(token);
        var candidate = candidates.Count == 1 ? candidates[0] : null;
        return candidate is not null && HospitalRecordCodes.MatchesRecord(candidate, input.HospitalNumber, input.LastName, input.FirstName, input.DateOfBirth, input.MiddleName, input.Suffix) &&
            !(clinic.PortalUserId is not null && candidate.PortalUserId is not null && clinic.PortalUserId != candidate.PortalUserId) ? candidate : null;
    }

    private async Task<IActionResult> MappingError(int id, RedStarHospitalMappingInput input, CancellationToken token)
    {
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        return clinic is null ? NotFound() : View("Mapping", new RedStarMappingPage(clinic, input, null));
    }
    private async Task<IActionResult> UploadForm(int id, RedStarLabUploadInput input, CancellationToken token)
    {
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token);
        if (clinic is null) return NotFound();
        var hospital = await access.MappedPatientAsync(id, token);
        if (hospital is null) { TempData["RedStarError"] = "Verify the hospital mapping before adding requests or reports."; return RedirectToAction(nameof(Mapping), new { id }); }
        if (string.IsNullOrEmpty(input.PatientVersion)) input.PatientVersion = Convert.ToBase64String(clinic.RowVersion);
        var encounters = await db.ClinicalEncounters.AsNoTracking().Where(e => e.PatientRecordId == hospital.Id).OrderByDescending(e => e.EncounterDate).ThenByDescending(e => e.Id).ToListAsync(token);
        var pending = await db.RedStarLabReports.AsNoTracking().Where(r => r.RedStarPatientId == id && r.PatientRecordId == hospital.Id && r.Status == "In progress" && r.ReportFileName == null).ToListAsync(token);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View private upload form", $"Patient/{id}", token);
        return View("Upload", new RedStarUploadPage(clinic, hospital, encounters, pending, input));
    }
    private async Task<IActionResult> EditForm(RedStarLabReport report, RedStarLabEditInput input, CancellationToken token)
    {
        var hospital = await access.MappedPatientAsync(report.RedStarPatientId, token);
        if (hospital is null) { TempData["RedStarError"] = "Verify the hospital mapping before editing this report."; return RedirectToAction(nameof(Index), new { id = report.RedStarPatientId }); }
        var encounters = await db.ClinicalEncounters.AsNoTracking().Where(e => e.PatientRecordId == hospital.Id).OrderByDescending(e => e.EncounterDate).ThenByDescending(e => e.Id).ToListAsync(token);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View private report edit", $"LabReport/{report.Id}", token);
        return View("Edit", new RedStarEditReportPage(report, hospital, encounters, input));
    }
    private void ValidateRelease(string status, DateTime? collectedAt, DateTime? releasedAt, bool hasPdf)
    {
        if (collectedAt is { Kind: not DateTimeKind.Unspecified } || releasedAt is { Kind: not DateTimeKind.Unspecified }) ModelState.AddModelError("", "Enter Manila wall time without a time-zone suffix.");
        if (hasPdf && !collectedAt.HasValue) ModelState.AddModelError("", "An attached PDF requires the actual test date.");
        if (status == "Available" && (!collectedAt.HasValue || !releasedAt.HasValue || !hasPdf)) ModelState.AddModelError("", "Release requires a test date, release time and PDF.");
        if (status == "Pending Verification" && !hasPdf) ModelState.AddModelError("", "A report awaiting verification requires an attached PDF.");
        if (status == "Cancelled" && hasPdf) ModelState.AddModelError("", "Remove the attached PDF before cancelling this request, or withdraw the report from patient access.");
        if (releasedAt < collectedAt) ModelState.AddModelError("", "Release cannot precede the test date.");
        if (collectedAt?.Date > ClinicalClock.Today) ModelState.AddModelError("", "The actual test date cannot be in the future.");
    }
    private static bool MatchesVersion(byte[] actual, string? submitted) => AdminAudit.Version(submitted) is { } expected && actual.SequenceEqual(expected);
    private void AddError(WriteResult result) { if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict; ModelState.AddModelError("", result.Error ?? "The change could not be saved."); }
}
