using System.Security.Claims;
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

[Area("Admin"), Authorize(Policy = AdminPolicies.RedStar), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RedStarImportsController(ApplicationDbContext db, RedStarImports imports) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("An authenticated actor is required.");

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        var count = await db.RedStarImportBatches.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
        var batches = await db.RedStarImportBatches.AsNoTracking().OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * 25).Take(25).Select(x => new RedStarImportBatch
            { Id = x.Id, CreatedUtc = x.CreatedUtc, ApprovedUtc = x.ApprovedUtc }).ToListAsync(cancellationToken);
        await RedStarAuditTrail.RecordAsync(db, Actor, "ImportListView", "PrivateImports", cancellationToken);
        return View(new RedStarImportIndex(batches, page, count));
    }

    [HttpPost, RequestSizeLimit(RedStarRegistryParser.MaxBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = RedStarRegistryParser.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || !Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || file.Length is <= 0 or > RedStarRegistryParser.MaxBytes)
        { TempData["RedStarImportMessage"] = "Select a non-empty .xlsx registry of at most 10 MB."; return RedirectToAction(nameof(Index)); }
        try
        {
            using var bytes = new MemoryStream();
            await using var stream = file.OpenReadStream();
            var buffer = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (bytes.Length + read > RedStarRegistryParser.MaxBytes) throw new ImportRejectedException("The upload exceeds 10 MB.");
                await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var (id, result) = await imports.StageAsync(bytes.ToArray(), Actor, cancellationToken);
            if (result.Succeeded) return RedirectToAction(nameof(Reconcile), new { id });
            TempData["RedStarImportMessage"] = result.Error;
        }
        catch (ImportRejectedException error) { TempData["RedStarImportMessage"] = error.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Reconcile(Guid id, int page = 1, CancellationToken cancellationToken = default)
    {
        var batch = await db.RedStarImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, Actor, "ImportPreviewView", id.ToString("N"), cancellationToken);
        try
        {
            var preview = imports.Read(batch);
            page = Math.Clamp(page, 1, Math.Max(1, (preview.Rows.Count + 24) / 25));
            var rows = preview.Rows.Skip((page - 1) * 25).Take(25).ToArray();
            var choices = rows.Select(x => preview.Choices.SingleOrDefault(c => c.Key == x.Key) ?? RedStarImports.DefaultChoice(batch, x)).ToArray();
            var codes = rows.Select(x => x.PatientCode).Where(x => x.Length != 0).ToArray();
            var numbers = rows.Select(x => x.HospitalNumber).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            var matches = await db.RedStarPatients.AsNoTracking().Where(x => codes.Contains(x.PatientCode) || numbers.Contains(x.HospitalNumber))
                .Select(x => new { x.Id, x.PatientCode, x.CodeName, x.HospitalNumber, x.RowVersion }).Take(250).ToListAsync(cancellationToken);
            var candidates = rows.ToDictionary(x => x.Key, x => (IReadOnlyList<RedStarImportCandidate>)matches
                .Where(m => (!string.IsNullOrEmpty(x.PatientCode) && string.Equals(x.PatientCode, m.PatientCode, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(x.HospitalNumber) && string.Equals(x.HospitalNumber, m.HospitalNumber, StringComparison.OrdinalIgnoreCase)))
                .Select(m => new RedStarImportCandidate(m.Id, m.PatientCode, m.CodeName, Convert.ToBase64String(m.RowVersion))).ToArray());
            return View(new RedStarImportReview(batch, preview, rows, choices, candidates, page));
        }
        catch (ImportRejectedException error) { TempData["RedStarImportMessage"] = error.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost]
    public async Task<IActionResult> Reconcile(Guid id, RedStarReconcileInput input, CancellationToken cancellationToken)
    {
        // Blank excluded rows may have no name yet; validate only the rows the staff member includes.
        for (var index = 0; index < input.Choices.Count; index++)
            if (!input.Choices[index].Include)
                foreach (var key in ModelState.Keys.Where(x => x.StartsWith($"Choices[{index}].", StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) TempData["RedStarImportMessage"] = "Review the included rows: required names, codes, field lengths and dates must be valid.";
        else
        {
            try
            {
                var result = await imports.ReconcileAsync(id, input, Actor, cancellationToken);
                TempData["RedStarImportMessage"] = result.Succeeded ? "Reconciliation saved. Review your included rows, then approve the batch." : result.Error;
            }
            catch (ImportRejectedException error) { TempData["RedStarImportMessage"] = error.Message; }
        }
        return RedirectToAction(nameof(Reconcile), new { id, page = input.Page });
    }

    [HttpPost]
    public async Task<IActionResult> Approve(Guid id, string version, CancellationToken cancellationToken)
    {
        try
        {
            var result = await imports.ApproveAsync(id, version, Actor, cancellationToken);
            TempData["RedStarImportMessage"] = result.Succeeded ? "Included records imported. Verify current schedules in the registry before enabling reminders." : result.Error;
        }
        catch (ImportRejectedException error) { TempData["RedStarImportMessage"] = error.Message; }
        return RedirectToAction(nameof(Reconcile), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Source(Guid id, CancellationToken cancellationToken)
    {
        var batch = await db.RedStarImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (batch is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, Actor, "ImportSourceDownload", id.ToString("N"), cancellationToken);
        try { return File(imports.ReadSource(batch), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Registry-source.xlsx"); }
        catch (ImportRejectedException error) { TempData["RedStarImportMessage"] = error.Message; return RedirectToAction(nameof(Reconcile), new { id }); }
    }

    [HttpPost]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var total = await db.RedStarEnrollments.CountAsync(cancellationToken);
        if (total > RedStarRegistryParser.MaxRows)
        { TempData["RedStarImportMessage"] = "The registry exceeds the 5,000-record export limit. Contact the system maintainer for a protected bulk export."; return RedirectToAction(nameof(Index)); }
        var rows = await db.RedStarEnrollments.AsNoTracking().Include(x => x.Patient).Include(x => x.Dispenses).Include(x => x.Appointments)
            .OrderBy(x => x.Patient.PatientCode).ThenBy(x => x.Program).ToListAsync(cancellationToken);
        await RedStarAuditTrail.RecordAsync(db, Actor, "RegistryExport", "PrivateRegistry", cancellationToken);
        return File(RedStarRegistryParser.Export(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Registry-core.xlsx");
    }
}
