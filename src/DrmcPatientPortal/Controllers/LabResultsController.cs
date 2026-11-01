using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/LabResults")]
public class LabResultsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly IOptionsMonitor<PatientResultsOptions> _resultsOptions;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ILabReportStorage _storage;
    private readonly PatientReportQueries _reports;

    public LabResultsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        IOptionsMonitor<PatientResultsOptions> resultsOptions,
        SignInManager<ApplicationUser> signIn,
        ILabReportStorage storage, PatientReportQueries reports)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _resultsOptions = resultsOptions;
        _signIn = signIn;
        _storage = storage;
        _reports = reports;
    }

    // Availability metadata remains visible while its PDF awaits release.
    [HttpGet("")]
    public async Task<IActionResult> Index(LabCategory? category, string? search, string? dateRange = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int legacyPage = 1, string? status = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        search = PatientReportQueries.NormalizeSearch(search);
        status = PatientReportQueries.NormalizeStatus(status);
        dateRange = dateRange is "30d" or "6m" or "year" or "custom" ? dateRange : null;
        if (category.HasValue && !Enum.IsDefined(category.Value)) category = null;
        var bounds = PatientReportQueries.DateBounds(dateRange, startDate, endDate);
        if (bounds.Error is not null) ViewData["DateRangeError"] = bounds.Error;
        var owned = await _reports.LaboratoriesAsync(user.Id, HttpContext.RequestAborted);
        var filtered = category.HasValue ? owned.Where(r => r.Category == category) : owned;
        var results = PatientReportQueries.Page(filtered, search, status, bounds.Start, bounds.End,
            dateRange != null && bounds.Error == null, page, legacyPage);
        await _auditLog.LogAsync(user.Id, "VIEW_LAB_LIST", "Patient/LabResults", "Viewed owned laboratory availability.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(new LabResultsIndexViewModel
        {
            SelectedCategory = category, SearchTerm = search, SelectedDateRange = dateRange,
            SelectedStartDate = startDate, SelectedEndDate = endDate, SelectedStatus = status,
            Results = results, TotalCount = owned.Count
        });
    }

    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var result = (await _reports.LaboratoriesAsync(user.Id, HttpContext.RequestAborted))
            .SingleOrDefault(r => r.Source == "lab" && r.Id == id);
        if (result is null) return NotFound();
        await _auditLog.LogAsync(user.Id, "VIEW_LAB_REPORT", $"LabResult/{id}", "Viewed owned laboratory request metadata.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View("Details", result);
    }

    [HttpPost("Report/{id:int}"), ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Report(int id, string? password, string? intent, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var lab = await SqlReadRetry.RunAsync(_db, () => _db.LabResults.AsNoTracking().Where(PatientResultsDisclosure.PdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow)).SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token), token);
        if (lab is null) return NotFound();
        if (intent is not ("view" or "download")) return BadRequest();
        if (string.IsNullOrEmpty(password) || password.Length > 1024)
            return await PasswordError(id, "Enter your login password for each View or Download action.");
        var locked = await _userManager.IsLockedOutAsync(user);
        var verified = !locked && await _signIn.CanSignInAsync(user) && await _userManager.CheckPasswordAsync(user, password);
        if (!verified)
            return await PasswordError(id, locked ? "Your account is temporarily locked. Try again after the lockout period." : "The password could not be verified. Please try again.");
        byte[] bytes;
        try { bytes = await _storage.ReadAsync(id, lab!.ReportFileName!, token); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        var fresh = await SqlReadRetry.RunAsync(_db, () => _db.LabResults.AsNoTracking().Where(PatientResultsDisclosure.PdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow)).SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token), token);
        if (fresh is null || fresh.ReportFileName != lab!.ReportFileName) return NotFound();
        try { await _auditLog.LogAsync(user.Id, intent == "view" ? "VIEW_LAB_PDF" : "DOWNLOAD_LAB_PDF", $"LabResult/{id}",
            intent == "view" ? "Viewed laboratory report PDF." : "Downloaded laboratory report PDF.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""); }
        catch (AuditLogPersistenceException) { return StatusCode(503); }
        fresh = await SqlReadRetry.RunAsync(_db, () => _db.LabResults.AsNoTracking().Where(PatientResultsDisclosure.PdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow)).SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token), token);
        if (fresh is null || fresh.ReportFileName != lab.ReportFileName || await _userManager.IsLockedOutAsync(user)) return NotFound();
        return new PrivateFileResult(bytes, "application/pdf", $"lab-report-{id}.pdf", intent == "download");
    }

    private async Task<IActionResult> PasswordError(int id, string message)
    {
        ModelState.Clear(); // Never return the submitted password in the page.
        ModelState.AddModelError("", message);
        return await Details(id);
    }
}
