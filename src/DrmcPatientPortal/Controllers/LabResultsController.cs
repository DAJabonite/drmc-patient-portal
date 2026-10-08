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

    public LabResultsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        IOptionsMonitor<PatientResultsOptions> resultsOptions,
        SignInManager<ApplicationUser> signIn,
        ILabReportStorage storage)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _resultsOptions = resultsOptions;
        _signIn = signIn;
        _storage = storage;
    }

    // GET /Patient/LabResults
    [HttpGet("")]
    public async Task<IActionResult> Index(
        LabCategory? category,
        string? search,
        string? dateRange = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _db.LabResults
            .Include(l => l.Items)
            .Where(l => l.Patient.PortalUserId == user.Id)
            .AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(l => l.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            s = s.Length > 200 ? s[..200] : s;
            query = query.Where(l => l.TestName.ToLower().Contains(s) || l.AccessionNumber.ToLower().Contains(s));
        }

        var today = ClinicalClock.Today;

        if (dateRange == "30d")
        {
            var rangeStart = today.AddDays(-30);
            query = query.Where(l => l.CollectedAt >= rangeStart);
        }
        else if (dateRange == "6m")
        {
            var rangeStart = today.AddMonths(-6);
            query = query.Where(l => l.CollectedAt >= rangeStart);
        }
        else if (dateRange == "year")
        {
            var rangeStart = new DateTime(today.Year, 1, 1);
            query = query.Where(l => l.CollectedAt >= rangeStart);
        }
        else if (dateRange == "custom")
        {
            if (!startDate.HasValue || !endDate.HasValue)
            {
                ViewData["DateRangeError"] = "Choose both a start date and an end date.";
            }
            else if (startDate.Value.Date > endDate.Value.Date)
            {
                ViewData["DateRangeError"] = "The start date must be on or before the end date.";
            }
            else if (startDate.Value.Date > today || endDate.Value.Date > today)
            {
                ViewData["DateRangeError"] = "Laboratory result dates cannot be in the future.";
            }
            else
            {
                var inclusiveStart = startDate.Value.Date;
                var exclusiveEnd = endDate.Value.Date.AddDays(1);
                query = query.Where(l => l.CollectedAt >= inclusiveStart && l.CollectedAt < exclusiveEnd);
            }
        }

        var allCount = await _db.LabResults.CountAsync(l => l.Patient.PortalUserId == user.Id);

        var results = await query
            .OrderByDescending(l => l.CollectedAt)
            .ToListAsync();

        var model = new LabResultsIndexViewModel
        {
            SelectedCategory = category,
            SearchTerm = search,
            SelectedDateRange = dateRange,
            SelectedStartDate = startDate,
            SelectedEndDate = endDate,
            LabResults = results,
            TotalCount = allCount,
            TotalAvailable = results.Count(r => r.Status == "Available"),
            TotalInProgress = results.Count(r => r.Status != "Available")
        };
        ViewData["PdfDisclosureEnabled"] = _resultsOptions.CurrentValue.ShowFullResults;

        return View(model);
    }

    // GET /Patient/LabResults/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await _db.LabResults
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id);

        if (result is null)
        {
            return NotFound();
        }

        // D4: item values and the result summary are shown only for released results with the switch on.
        // ClinicalNotes stay staff-only either way.
        var showFullResults = _resultsOptions.CurrentValue.ShowFullResults
            && PatientResultsDisclosure.IsReleased(result, PatientResultsDisclosure.ManilaNow);
        ViewData["ShowFullResults"] = showFullResults;
        ViewData["CanAccessPdf"] = showFullResults && result.ReportFileName is not null;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_LAB_REPORT", $"LabResult/{id}",
            showFullResults ? "Viewed released laboratory result values." : "Viewed laboratory report availability.", ip);

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
        var lab = await _db.LabResults.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token);
        if (!Eligible(lab)) return NotFound();
        if (intent is not ("view" or "download")) return BadRequest();
        if (string.IsNullOrEmpty(password) || password.Length > 1024)
            return await PasswordError(id, "Enter your login password for each View or Download action.");
        var verified = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!verified.Succeeded)
            return await PasswordError(id, verified.IsLockedOut ? "Your account is temporarily locked. Try again after the lockout period." : "The password could not be verified. Please try again.");
        byte[] bytes;
        try { bytes = await _storage.ReadAsync(id, lab!.ReportFileName!, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { return StatusCode(503); }
        var fresh = await _db.LabResults.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token);
        if (!Eligible(fresh) || fresh!.ReportFileName != lab!.ReportFileName) return NotFound();
        await _auditLog.LogAsync(user.Id, intent == "view" ? "VIEW_LAB_PDF" : "DOWNLOAD_LAB_PDF", $"LabResult/{id}",
            intent == "view" ? "Viewed laboratory report PDF." : "Downloaded laboratory report PDF.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
        fresh = await _db.LabResults.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id && l.Patient.PortalUserId == user.Id, token);
        if (!Eligible(fresh) || fresh!.ReportFileName != lab.ReportFileName || await _userManager.IsLockedOutAsync(user)) return NotFound();
        Response.Headers.ContentDisposition = $"{(intent == "view" ? "inline" : "attachment")}; filename=\"lab-report-{id}.pdf\"";
        return File(bytes, "application/pdf");
    }

    private bool Eligible(LabResult? lab) => lab?.ReportFileName is not null && _resultsOptions.CurrentValue.ShowFullResults
        && PatientResultsDisclosure.IsReleased(lab, PatientResultsDisclosure.ManilaNow);

    private async Task<IActionResult> PasswordError(int id, string message)
    {
        ModelState.Clear(); // Never return the submitted password in the page.
        ModelState.AddModelError("", message);
        return await Details(id);
    }
}

public class LabResultsIndexViewModel
{
    public LabCategory? SelectedCategory { get; set; }
    public string? SearchTerm { get; set; }
    public string? SelectedDateRange { get; set; }
    public DateTime? SelectedStartDate { get; set; }
    public DateTime? SelectedEndDate { get; set; }
    public IReadOnlyList<LabResult> LabResults { get; set; } = Array.Empty<LabResult>();
    public int TotalCount { get; set; }
    public int TotalAvailable { get; set; }
    public int TotalInProgress { get; set; }
}
