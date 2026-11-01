using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

// Request metadata is owned; attached PDFs independently enforce disclosure and release rules.
[Authorize]
[Route("Patient/Radiology")]
public class RadiologyController : Controller
{

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly IOptionsMonitor<PatientResultsOptions> _resultsOptions;
    private readonly InstitutionalGuideService _guides;
    private readonly IRadiologyReportStorage _storage;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly PatientReportQueries _reports;

    public RadiologyController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        IOptionsMonitor<PatientResultsOptions> resultsOptions,
        InstitutionalGuideService guides,
        IRadiologyReportStorage storage,
        SignInManager<ApplicationUser> signIn, PatientReportQueries reports)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _resultsOptions = resultsOptions;
        _guides = guides;
        _storage = storage;
        _signIn = signIn;
        _reports = reports;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(RadiologyModality? modality, string? search, string? dateRange = null,
        DateTime? startDate = null, DateTime? endDate = null, int page = 1, int legacyPage = 1, string? status = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        search = PatientReportQueries.NormalizeSearch(search);
        status = PatientReportQueries.NormalizeStatus(status);
        dateRange = dateRange is "30d" or "6m" or "year" or "custom" ? dateRange : null;
        if (modality.HasValue && !Enum.IsDefined(modality.Value)) modality = null;
        var bounds = PatientReportQueries.DateBounds(dateRange, startDate, endDate);
        if (bounds.Error is not null) ViewData["DateRangeError"] = bounds.Error;
        var owned = await _reports.RadiologyAsync(user.Id, HttpContext.RequestAborted);
        var filtered = modality.HasValue ? owned.Where(r => r.Modality == modality) : owned;
        var results = PatientReportQueries.Page(filtered, search, status, bounds.Start, bounds.End,
            dateRange != null && bounds.Error == null, page, legacyPage);
        await _auditLog.LogAsync(user.Id, "VIEW_RADIOLOGY_LIST", "Patient/Radiology", "Viewed owned imaging availability.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(new RadiologyIndexViewModel
        {
            PreparationGuide = await _guides.GetAsync("radiology-preparation", cancellationToken: HttpContext.RequestAborted),
            SelectedModality = modality, SearchTerm = search, SelectedDateRange = dateRange,
            SelectedStartDate = startDate, SelectedEndDate = endDate, SelectedStatus = status,
            Results = results, TotalCount = owned.Count
        });
    }

    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var study = (await _reports.RadiologyAsync(user.Id, HttpContext.RequestAborted)).SingleOrDefault(r => r.Id == id);
        if (study is null) return NotFound();
        await _auditLog.LogAsync(user.Id, "VIEW_RADIOLOGY_REPORT", $"RadiologyStudy/{id}", "Viewed owned imaging request metadata.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View("Details", study);
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
        Task<RadiologyStudy?> Current() => SqlReadRetry.RunAsync(_db, () => _db.RadiologyStudies.AsNoTracking()
            .Where(PatientResultsDisclosure.RadiologyPdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow))
            .SingleOrDefaultAsync(s => s.Id == id && s.Patient.PortalUserId == user.Id, token), token);
        var study = await Current();
        if (study is null) return NotFound();
        if (intent is not ("view" or "download")) return BadRequest();
        if (string.IsNullOrEmpty(password) || password.Length > 1024)
            return await PasswordError(id, "Enter your login password for each View or Download action.");
        var locked = await _userManager.IsLockedOutAsync(user);
        if (locked || !await _signIn.CanSignInAsync(user) || !await _userManager.CheckPasswordAsync(user, password))
            return await PasswordError(id, locked ? "Your account is temporarily locked. Try again after the lockout period." : "The password could not be verified. Please try again.");
        byte[] bytes;
        try { bytes = await _storage.ReadAsync(id, study.ReportFileName!, token); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        var fresh = await Current();
        if (fresh is null || fresh.ReportFileName != study.ReportFileName) return NotFound();
        try
        {
            await _auditLog.LogAsync(user.Id, intent == "view" ? "VIEW_RADIOLOGY_PDF" : "DOWNLOAD_RADIOLOGY_PDF",
                $"RadiologyStudy/{id}", "Patient accessed released radiology PDF.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        }
        catch (AuditLogPersistenceException) { return StatusCode(503); }
        fresh = await Current();
        if (fresh is null || fresh.ReportFileName != study.ReportFileName || await _userManager.IsLockedOutAsync(user)) return NotFound();
        return new PrivateFileResult(bytes, "application/pdf", $"radiology-report-{id}.pdf", intent == "download");
    }

    private async Task<IActionResult> PasswordError(int id, string message)
    {
        ModelState.Clear();
        ModelState.AddModelError("", message);
        return await Details(id);
    }
}
