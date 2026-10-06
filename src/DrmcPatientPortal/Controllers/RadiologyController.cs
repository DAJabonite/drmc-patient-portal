using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

// Patient view of their own imaging studies. Every query is scoped to the signed-in user's
// patient record; report text is disclosed only by PatientResultsDisclosure plus D4.
[Authorize]
[Route("Patient/Radiology")]
public class RadiologyController : Controller
{
    private const int MaxSearchLength = 200;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly IOptionsMonitor<PatientResultsOptions> _resultsOptions;

    public RadiologyController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        IOptionsMonitor<PatientResultsOptions> resultsOptions)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _resultsOptions = resultsOptions;
    }

    // GET /Patient/Radiology
    [HttpGet("")]
    public async Task<IActionResult> Index(
        RadiologyModality? modality,
        string? search,
        string? dateRange = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var owned = _db.RadiologyStudies.Where(s => s.Patient.PortalUserId == user.Id);
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

        var rows = await query
            .OrderByDescending(s => s.PerformedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.AccessionNumber, s.StudyName, s.Modality, s.BodyRegion,
                s.PerformedAt, s.ReleasedAt, s.Status, s.OrderingPhysician
            })
            .ToListAsync();

        var now = PatientResultsDisclosure.ManilaNow;
        var studies = rows.Select(r =>
        {
            var ready = PatientResultsDisclosure.IsReleased(r.Status, r.ReleasedAt, now);
            return new RadiologyListItem
            {
                Id = r.Id,
                AccessionNumber = r.AccessionNumber,
                StudyName = r.StudyName,
                Modality = r.Modality,
                BodyRegion = r.BodyRegion,
                PerformedAt = r.PerformedAt,
                ReleasedAt = ready ? r.ReleasedAt : null,
                OrderingPhysician = r.OrderingPhysician,
                IsReady = ready
            };
        }).ToList();

        return View(new RadiologyIndexViewModel
        {
            SelectedModality = modality,
            SearchTerm = search,
            SelectedDateRange = dateRange,
            SelectedStartDate = dateRange == "custom" ? startDate : null,
            SelectedEndDate = dateRange == "custom" ? endDate : null,
            Studies = studies,
            TotalCount = await owned.CountAsync()
        });
    }

    // GET /Patient/Radiology/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        // InternalNotes is intentionally not part of this projection.
        var study = await _db.RadiologyStudies
            .Where(s => s.Id == id && s.Patient.PortalUserId == user.Id)
            .Select(s => new
            {
                s.Id, s.AccessionNumber, s.StudyName, s.Modality, s.BodyRegion, s.PerformedAt,
                s.ReleasedAt, s.Status, s.OrderingPhysician, s.RadiologistName, s.PerformingUnit,
                s.ClinicalIndication, s.Technique, s.Comparison, s.Findings, s.Impression,
                s.PlainLanguageSummary, s.AmendmentNote
            })
            .FirstOrDefaultAsync();

        if (study is null) return NotFound();

        var ready = PatientResultsDisclosure.IsReleased(study.Status, study.ReleasedAt, PatientResultsDisclosure.ManilaNow);
        var disclose = ready && _resultsOptions.CurrentValue.ShowFullResults;

        var model = new RadiologyDetailsViewModel
        {
            Id = study.Id,
            AccessionNumber = study.AccessionNumber,
            StudyName = study.StudyName,
            Modality = study.Modality,
            BodyRegion = study.BodyRegion,
            PerformedAt = study.PerformedAt,
            ReleasedAt = ready ? study.ReleasedAt : null,
            OrderingPhysician = study.OrderingPhysician,
            RadiologistName = ready ? study.RadiologistName : string.Empty,
            PerformingUnit = study.PerformingUnit,
            IsReady = ready,
            IsAmended = ready && study.Status == RadiologyStatus.Amended,
            Report = disclose
                ? new RadiologyReportView
                {
                    ClinicalIndication = study.ClinicalIndication,
                    Technique = study.Technique,
                    Comparison = study.Comparison,
                    Findings = study.Findings,
                    Impression = study.Impression,
                    PlainLanguageSummary = study.PlainLanguageSummary,
                    AmendmentNote = study.AmendmentNote
                }
                : null
        };

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_RADIOLOGY_REPORT", $"RadiologyStudy/{id}",
            disclose ? "Viewed released imaging report." : "Viewed imaging study availability and claiming notice.", ip);

        return View(model);
    }
}
