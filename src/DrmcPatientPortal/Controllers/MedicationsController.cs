using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/Medications")]
public class MedicationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly RedStarPatientService _redStar;

    public MedicationsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog, RedStarPatientService redStar)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _redStar = redStar;
    }

    // GET /Patient/Medications
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int schedulePage = 1, int allergyPage = 1, int legacyPage = 1,
        string? dateRange = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var owned = await PrescriptionQueries.OwnedAsync(_db, user.Id, HttpContext.RequestAborted);
        dateRange = dateRange is "30d" or "6m" or "year" or "custom" ? dateRange : null;
        var bounds = PatientReportQueries.DateBounds(dateRange, startDate, endDate);
        if (bounds.Error != null) ViewData["DateRangeError"] = bounds.Error;
        var filtered = owned.Where(p => p.Encounter != null
            && (!bounds.Start.HasValue || p.Encounter.EncounterDate >= bounds.Start)
            && (!bounds.End.HasValue || p.Encounter.EncounterDate < bounds.End));
        var groups = filtered.GroupBy(p => p.ClinicalEncounterId).Select(g => new
        {
            Id = g.Key, Active = g.Any(p => p.Status == PrescriptionStatus.Active),
            Activity = g.Max(p => p.PatientActivityAtUtc ?? p.PrescribedAt), Date = g.Max(p => p.Encounter!.EncounterDate)
        });
        var pagination = await PortalPagination.CreateAsync(groups, _db, page);
        var encounterIds = await SqlReadRetry.RunAsync(_db, () => groups.OrderByDescending(g => g.Active)
            .ThenByDescending(g => g.Activity).ThenByDescending(g => g.Date).ThenByDescending(g => g.Id)
            .Skip(pagination.Offset).Take(PortalPagination.Size).Select(g => g.Id).ToArrayAsync());
        var selected = await SqlReadRetry.RunAsync(_db, () => filtered
            .Where(p => EF.Parameter(encounterIds).Contains(p.ClinicalEncounterId)).ToListAsync());
        var prescriptionGroups = encounterIds.Select(id => selected.Where(p => p.ClinicalEncounterId == id)
            .OrderByDescending(p => p.Status == PrescriptionStatus.Active).ThenByDescending(p => p.PrescribedAt).ThenByDescending(p => p.Id).ToList())
            .Where(prescriptions => prescriptions.Count > 0).Select(prescriptions => new PrescriptionEncounterGroup(
                prescriptions[0].ClinicalEncounterId, prescriptions[0].Encounter!.EncounterReference,
                prescriptions[0].Encounter!.EncounterDate, prescriptions)).ToList();
        var legacy = owned.Where(p => p.ClinicalEncounterId == null && (dateRange == null || bounds.Error != null));
        var legacyPagination = await PortalPagination.CreateAsync(legacy, _db, legacyPage, "legacyPage");
        var prescriptions = await SqlReadRetry.RunAsync(_db, () => legacy.OrderByDescending(p => p.Status == PrescriptionStatus.Active)
            .ThenByDescending(p => p.PrescribedAt).ThenByDescending(p => p.Id).Skip(legacyPagination.Offset).Take(PortalPagination.Size).ToListAsync());

        var allergyQuery = _db.PatientAllergies.AsNoTracking().Where(a => a.Patient.PortalUserId == user.Id);
        var allergyPagination = await PortalPagination.CreateAsync(allergyQuery, _db, allergyPage, "allergyPage");
        var allergies = await SqlReadRetry.RunAsync(_db, () => allergyQuery.OrderBy(a => a.Allergen).ThenBy(a => a.Id)
            .Skip(allergyPagination.Offset).Take(PortalPagination.Size).ToListAsync());
        var scheduleQuery = _db.MedicationDoseSchedules.AsNoTracking()
            .Where(s => owned.Select(p => p.Id).Contains(s.PrescriptionId) && s.Prescription.Status == PrescriptionStatus.Active);
        var schedulePagination = await PortalPagination.CreateAsync(scheduleQuery, _db, schedulePage, "schedulePage");
        var schedules = await SqlReadRetry.RunAsync(_db, () => scheduleQuery.OrderBy(s => s.DoseTime).ThenBy(s => s.DisplayOrder).ThenBy(s => s.Id)
            .Skip(schedulePagination.Offset).Take(PortalPagination.Size)
            .Select(s => new MedicationScheduleItem(s.DoseTime, s.Prescription.GenericName, s.Prescription.Dosage)).ToListAsync());

        var model = new MedicationsIndexViewModel
        {
            RedStarEnrolled = (await _redStar.PortalStateAsync()).Enrolled,
            RedStarRefill = await _redStar.NextRefillAsync(user.Id),
            Prescriptions = prescriptions,
            Allergies = allergies,
            ActiveCount = await SqlReadRetry.RunAsync(_db, () => owned.CountAsync(p => p.Status == PrescriptionStatus.Active)),
            PrescriptionTotal = await SqlReadRetry.RunAsync(_db, () => filtered.CountAsync()) + legacyPagination.Total,
            PrescriptionGroups = prescriptionGroups, Pagination = pagination, LegacyPagination = legacyPagination,
            SelectedDateRange = dateRange, SelectedStartDate = startDate, SelectedEndDate = endDate,
            LegacyExcluded = dateRange != null && bounds.Error == null && await SqlReadRetry.RunAsync(_db, () => owned.AnyAsync(p => p.ClinicalEncounterId == null)),
            Schedules = schedules, SchedulePagination = schedulePagination, AllergyPagination = allergyPagination
        };

        await _auditLog.LogAsync(user.Id, "VIEW_MEDICATION_LIST", "Patient/Medications", "Viewed owned prescriptions, schedules and allergies.", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return View(model);
    }

    // GET /Patient/Medications/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var owned = await PrescriptionQueries.OwnedAsync(_db, user.Id, HttpContext.RequestAborted);
        var rx = await SqlReadRetry.RunAsync(_db, () => owned.FirstOrDefaultAsync(p => p.Id == id));

        if (rx is null)
        {
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_PRESCRIPTION", $"Prescription/{id}", "Viewed an owned prescription.", ip);

        return View(rx);
    }
}

public class MedicationsIndexViewModel
{
    public bool RedStarEnrolled { get; set; }
    public RedStarRefillCard? RedStarRefill { get; set; }
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = Array.Empty<Prescription>();
    public IReadOnlyList<PatientAllergy> Allergies { get; set; } = Array.Empty<PatientAllergy>();
    public IReadOnlyList<PrescriptionEncounterGroup> PrescriptionGroups { get; set; } = [];
    public PortalPagination Pagination { get; set; } = new(0, 1);
    public PortalPagination LegacyPagination { get; set; } = new(0, 1, "legacyPage");
    public string? SelectedDateRange { get; set; }
    public DateTime? SelectedStartDate { get; set; }
    public DateTime? SelectedEndDate { get; set; }
    public bool LegacyExcluded { get; set; }
    public int ActiveCount { get; set; }
    public int PrescriptionTotal { get; set; }
    public IReadOnlyList<MedicationScheduleItem> Schedules { get; set; } = [];
    public PortalPagination SchedulePagination { get; set; } = new(0, 1, "schedulePage");
    public PortalPagination AllergyPagination { get; set; } = new(0, 1, "allergyPage");
}

public sealed record MedicationScheduleItem(TimeOnly DoseTime, string GenericName, string Dosage);

public sealed record PrescriptionEncounterGroup(int? EncounterId, string Reference, DateTime? Date, IReadOnlyList<Prescription> Prescriptions);
