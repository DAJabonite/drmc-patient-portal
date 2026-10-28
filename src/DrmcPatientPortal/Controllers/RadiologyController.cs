using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

// Patient PDF metadata is always scoped to the signed-in user and the disclosure/release rules.
[Authorize]
[Route("Patient/Radiology")]
public class RadiologyController : Controller
{
    private const int MaxSearchLength = 200;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly IOptionsMonitor<PatientResultsOptions> _resultsOptions;
    private readonly InstitutionalGuideService _guides;
    private readonly IRadiologyReportStorage _storage;
    private readonly SignInManager<ApplicationUser> _signIn;

    public RadiologyController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        IOptionsMonitor<PatientResultsOptions> resultsOptions,
        InstitutionalGuideService guides,
        IRadiologyReportStorage storage,
        SignInManager<ApplicationUser> signIn)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _resultsOptions = resultsOptions;
        _guides = guides;
        _storage = storage;
        _signIn = signIn;
    }

    // GET /Patient/Radiology
    [HttpGet("")]
    public async Task<IActionResult> Index(
        RadiologyModality? modality,
        string? search,
        string? dateRange = null,
        DateTime? startDate = null,
        DateTime? endDate = null, int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var owned = _db.RadiologyStudies.AsNoTracking()
            .Where(PatientResultsDisclosure.RadiologyPdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow))
            .Where(s => s.Patient.PortalUserId == user.Id);
        var query = owned;

        if (modality.HasValue && Enum.IsDefined(modality.Value))
        {
            query = query.Where(s => s.Modality == modality.Value);
        }
        else
        {
            modality = null;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            if (search.Length > MaxSearchLength) search = search[..MaxSearchLength];
            var s = search.ToLower();
            query = query.Where(r => r.StudyName.ToLower().Contains(s)
                                  || r.AccessionNumber.ToLower().Contains(s)
                                  || r.BodyRegion.ToLower().Contains(s));
        }
        else
        {
            search = null;
        }

        var today = ClinicalClock.Today;
        switch (dateRange)
        {
            case "30d":
                var last30 = today.AddDays(-30);
                query = query.Where(s => s.PerformedAt >= last30);
                break;
            case "6m":
                var last6m = today.AddMonths(-6);
                query = query.Where(s => s.PerformedAt >= last6m);
                break;
            case "year":
                var yearStart = new DateTime(today.Year, 1, 1);
                query = query.Where(s => s.PerformedAt >= yearStart);
                break;
            case "custom":
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
                    ViewData["DateRangeError"] = "Imaging study dates cannot be in the future.";
                }
                else
                {
                    var inclusiveStart = startDate.Value.Date;
                    var exclusiveEnd = endDate.Value.Date.AddDays(1);
                    query = query.Where(s => s.PerformedAt >= inclusiveStart && s.PerformedAt < exclusiveEnd);
                }
                break;
            default:
                dateRange = null;
                break;
        }

        var token = HttpContext.RequestAborted;
        var dateQuery = query.Select(s => s.PerformedAt.Date).Distinct();
        var pagination = await PortalPagination.CreateAsync(dateQuery, _db, page);
        ViewData["Pagination"] = pagination;
        var dates = await SqlReadRetry.RunAsync(_db, () => dateQuery.OrderByDescending(date => date)
            .Skip(pagination.Offset).Take(PortalPagination.Size).ToListAsync(token), token);
        List<RadiologyListItem> studies = dates.Count == 0 ? [] : await SqlReadRetry.RunAsync(_db, () => query
            .Where(s => dates.Contains(s.PerformedAt.Date))
            .OrderByDescending(s => s.PerformedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new RadiologyListItem
            {
                Id = s.Id, Modality = s.Modality, PerformedAt = s.PerformedAt
            })
            .ToListAsync(token), token);

        await _auditLog.LogAsync(user.Id, "VIEW_RADIOLOGY_LIST", "Patient/Radiology", "Viewed owned imaging availability.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(new RadiologyIndexViewModel
        {
            PreparationGuide = await _guides.GetAsync("radiology-preparation", cancellationToken: HttpContext.RequestAborted),
            SelectedModality = modality,
            SearchTerm = search,
            SelectedDateRange = dateRange,
            SelectedStartDate = dateRange == "custom" ? startDate : null,
            SelectedEndDate = dateRange == "custom" ? endDate : null,
            DateGroups = studies.GroupBy(s => s.PerformedAt.Date)
                .Select(group => new RadiologyDateGroup(group.Key, group.ToList())).ToList(),
            TotalCount = await SqlReadRetry.RunAsync(_db, () => owned.CountAsync(token), token)
        });
    }

    // GET /Patient/Radiology/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var model = await SqlReadRetry.RunAsync(_db, () => _db.RadiologyStudies.AsNoTracking()
            .Where(PatientResultsDisclosure.RadiologyPdfEligibility(_resultsOptions.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow))
            .Where(s => s.Id == id && s.Patient.PortalUserId == user.Id)
            .Select(s => new RadiologyDetailsViewModel
            {
                Id = s.Id, Modality = s.Modality, PerformedAt = s.PerformedAt
            })
            .FirstOrDefaultAsync());

        if (model is null) return NotFound();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_RADIOLOGY_REPORT", $"RadiologyStudy/{id}",
            "Viewed released imaging report.", ip);

        return View("Details", model);
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
