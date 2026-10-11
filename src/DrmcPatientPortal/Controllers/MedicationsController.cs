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

    public MedicationsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    // GET /Patient/Medications
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int schedulePage = 1, int allergyPage = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var owned = _db.Prescriptions.AsNoTracking().Where(p => p.Patient.PortalUserId == user.Id);
        var pagination = await PortalPagination.CreateAsync(owned, _db, page);
        ViewData["Pagination"] = pagination;
        var prescriptions = await SqlReadRetry.RunAsync(_db, () => owned
            .OrderByDescending(p => p.Status == PrescriptionStatus.Active)
            .ThenByDescending(p => p.PrescribedAt)
            .ThenByDescending(p => p.Id).Skip(pagination.Offset).Take(PortalPagination.Size).ToListAsync());

        var allergyQuery = _db.PatientAllergies.AsNoTracking().Where(a => a.Patient.PortalUserId == user.Id);
        var allergyPagination = await PortalPagination.CreateAsync(allergyQuery, _db, allergyPage, "allergyPage");
        var allergies = await SqlReadRetry.RunAsync(_db, () => allergyQuery.OrderBy(a => a.Allergen).ThenBy(a => a.Id)
            .Skip(allergyPagination.Offset).Take(PortalPagination.Size).ToListAsync());
        var scheduleQuery = _db.MedicationDoseSchedules.AsNoTracking()
            .Where(s => s.Prescription.Patient.PortalUserId == user.Id && s.Prescription.Status == PrescriptionStatus.Active);
        var schedulePagination = await PortalPagination.CreateAsync(scheduleQuery, _db, schedulePage, "schedulePage");
        var schedules = await SqlReadRetry.RunAsync(_db, () => scheduleQuery.OrderBy(s => s.DoseTime).ThenBy(s => s.DisplayOrder).ThenBy(s => s.Id)
            .Skip(schedulePagination.Offset).Take(PortalPagination.Size)
            .Select(s => new MedicationScheduleItem(s.DoseTime, s.Prescription.GenericName, s.Prescription.Dosage)).ToListAsync());

        var model = new MedicationsIndexViewModel
        {
            Prescriptions = prescriptions,
            Allergies = allergies,
            ActiveCount = await SqlReadRetry.RunAsync(_db, () => owned.CountAsync(p => p.Status == PrescriptionStatus.Active)),
            PrescriptionTotal = pagination.Total,
            Schedules = schedules, SchedulePagination = schedulePagination, AllergyPagination = allergyPagination
        };

        return View(model);
    }

    // GET /Patient/Medications/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var rx = await SqlReadRetry.RunAsync(_db, () => _db.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == id && p.Patient.PortalUserId == user.Id));

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
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = Array.Empty<Prescription>();
    public IReadOnlyList<PatientAllergy> Allergies { get; set; } = Array.Empty<PatientAllergy>();
    public int ActiveCount { get; set; }
    public int PrescriptionTotal { get; set; }
    public IReadOnlyList<MedicationScheduleItem> Schedules { get; set; } = [];
    public PortalPagination SchedulePagination { get; set; } = new(0, 1, "schedulePage");
    public PortalPagination AllergyPagination { get; set; } = new(0, 1, "allergyPage");
}

public sealed record MedicationScheduleItem(TimeOnly DoseTime, string GenericName, string Dosage);
