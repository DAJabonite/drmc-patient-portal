using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize]
public class PatientController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public PatientController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Home()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var nextAppointment = await _db.Appointments
            .Where(a => a.PatientUserId == user.Id && a.Status != "Cancelled")
            .OrderBy(a => a.ScheduledAt)
            .FirstOrDefaultAsync();

        bool hasTriage = false;
        if (nextAppointment is not null)
        {
            hasTriage = await _db.TriageIntakes.AnyAsync(t => t.AppointmentId == nextAppointment.Id);
        }

        var latestLab = await _db.LabResults
            .Where(l => l.PatientUserId == user.Id)
            .OrderByDescending(l => l.CollectedAt)
            .ToListAsync();

        var activePrescriptions = await _db.Prescriptions
            .CountAsync(p => p.PatientUserId == user.Id && p.Status == PrescriptionStatus.Active);

        var model = new PatientDashboardViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ContactNumber = user.ContactNumber,
            CreatedAt = user.CreatedAt,
            NextAppointment = nextAppointment,
            HasTriageForNextAppointment = hasTriage,
            LabResults = latestLab,
            ActivePrescriptionsCount = activePrescriptions,
            Departments = ClinicalDepartments.All,
        };

        return View(model);
    }

    // GET /Patient/Audit
    [HttpGet("Patient/Audit")]
    public async Task<IActionResult> Audit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var logs = await _db.AuditLogs
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.Timestamp)
            .Take(50)
            .ToListAsync();

        var model = new PatientAuditViewModel
        {
            PatientName = user.FullName,
            Email = user.Email ?? string.Empty,
            Logs = logs
        };

        return View(model);
    }
}

public class PatientDashboardViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Appointment? NextAppointment { get; set; }
    public bool HasTriageForNextAppointment { get; set; }
    public IReadOnlyList<LabResult> LabResults { get; set; } = Array.Empty<LabResult>();
    public int ActivePrescriptionsCount { get; set; }
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}

public class PatientAuditViewModel
{
    public string PatientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<AuditLog> Logs { get; set; } = Array.Empty<AuditLog>();
}
