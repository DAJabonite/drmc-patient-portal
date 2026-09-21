using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/Encounters")]
public class EncountersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public EncountersController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    // GET /Patient/Encounters
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? department,
        string? dateRange,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _db.ClinicalEncounters
            .Where(e => e.PatientUserId == user.Id)
            .AsQueryable();

        var selectedType = department?.Trim();
        if (string.Equals(selectedType, "OPD", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => e.Type == EncounterType.OpdConsultation || e.Type == EncounterType.Teleconsultation);
        }
        else if (string.Equals(selectedType, "Emergency", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => e.Type == EncounterType.Emergency);
        }
        else if (string.Equals(selectedType, "Inpatient", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => e.Type == EncounterType.Inpatient);
        }
        else if (!string.IsNullOrWhiteSpace(selectedType))
        {
            query = query.Where(e => false);
        }

        var today = DateTime.Today;
        if (dateRange == "30d")
        {
            var rangeStart = today.AddDays(-30);
            query = query.Where(e => e.EncounterDate >= rangeStart);
        }
        else if (dateRange == "6m")
        {
            var rangeStart = today.AddMonths(-6);
            query = query.Where(e => e.EncounterDate >= rangeStart);
        }
        else if (dateRange == "year")
        {
            var rangeStart = new DateTime(today.Year, 1, 1);
            query = query.Where(e => e.EncounterDate >= rangeStart);
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
                ViewData["DateRangeError"] = "Visit dates cannot be in the future.";
            }
            else
            {
                var inclusiveStart = startDate.Value.Date;
                var exclusiveEnd = endDate.Value.Date.AddDays(1);
                query = query.Where(e => e.EncounterDate >= inclusiveStart && e.EncounterDate < exclusiveEnd);
            }
        }

        var encounters = await query
            .OrderByDescending(e => e.EncounterDate)
            .ToListAsync();

        var model = new EncountersIndexViewModel
        {
            SelectedType = selectedType,
            SelectedDateRange = dateRange,
            SelectedStartDate = startDate,
            SelectedEndDate = endDate,
            Encounters = encounters,
        };

        return View(model);
    }

    // GET /Patient/Encounters/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var encounter = await _db.ClinicalEncounters
            .Include(e => e.LabResults)
            .FirstOrDefaultAsync(e => e.Id == id && e.PatientUserId == user.Id);

        if (encounter is null)
        {
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_ENCOUNTER_SUMMARY", $"ClinicalEncounter/{id}", "Viewed an owned encounter summary.", ip);

        return View(encounter);
    }
}

public class EncountersIndexViewModel
{
    public string? SelectedType { get; set; }
    public string? SelectedDateRange { get; set; }
    public DateTime? SelectedStartDate { get; set; }
    public DateTime? SelectedEndDate { get; set; }
    public IReadOnlyList<ClinicalEncounter> Encounters { get; set; } = Array.Empty<ClinicalEncounter>();
}
