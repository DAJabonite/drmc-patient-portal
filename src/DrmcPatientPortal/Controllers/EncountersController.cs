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
    public async Task<IActionResult> Index(string? department)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _db.ClinicalEncounters
            .Where(e => e.PatientUserId == user.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(e => e.Department == department);
        }

        var encounters = await query
            .OrderByDescending(e => e.EncounterDate)
            .ToListAsync();

        var model = new EncountersIndexViewModel
        {
            SelectedDepartment = department,
            Encounters = encounters,
            Departments = ClinicalDepartments.All
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
        await _auditLog.LogAsync(user.Id, "VIEW_ENCOUNTER_SUMMARY", $"ClinicalEncounter/{id}", $"Reference #{encounter.EncounterReference} - {encounter.Department}", ip);

        return View(encounter);
    }

    // GET /Patient/Encounters/Print/{id}
    [HttpGet("Print/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var encounter = await _db.ClinicalEncounters
            .FirstOrDefaultAsync(e => e.Id == id && e.PatientUserId == user.Id);

        if (encounter is null)
        {
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "PRINT_ENCOUNTER_SUMMARY", $"ClinicalEncounter/{id}", $"Printed consultation slip #{encounter.EncounterReference}", ip);

        var model = new EncounterPrintViewModel
        {
            Encounter = encounter,
            Patient = user,
            PrintedAt = DateTime.UtcNow
        };

        return View(model);
    }
}

public class EncountersIndexViewModel
{
    public string? SelectedDepartment { get; set; }
    public IReadOnlyList<ClinicalEncounter> Encounters { get; set; } = Array.Empty<ClinicalEncounter>();
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}

public class EncounterPrintViewModel
{
    public ClinicalEncounter Encounter { get; set; } = null!;
    public ApplicationUser Patient { get; set; } = null!;
    public DateTime PrintedAt { get; set; }
}
