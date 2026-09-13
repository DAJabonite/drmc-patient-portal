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
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var prescriptions = await _db.Prescriptions
            .Include(p => p.DoseSchedules)
            .Where(p => p.PatientUserId == user.Id)
            .OrderByDescending(p => p.Status == PrescriptionStatus.Active)
            .ThenByDescending(p => p.PrescribedAt)
            .ToListAsync();

        var allergies = await _db.PatientAllergies
            .Where(a => a.PatientUserId == user.Id)
            .ToListAsync();

        var model = new MedicationsIndexViewModel
        {
            Prescriptions = prescriptions,
            Allergies = allergies,
            ActiveCount = prescriptions.Count(p => p.Status == PrescriptionStatus.Active)
        };

        return View(model);
    }

    // GET /Patient/Medications/Details/{id}
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var rx = await _db.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == id && p.PatientUserId == user.Id);

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
}
