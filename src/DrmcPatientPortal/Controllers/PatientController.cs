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

        var unreadMessages = await _db.Messages
            .CountAsync(m => m.Thread.PatientUserId == user.Id && !m.IsRead && m.SenderRole != MessageSenderRole.Patient);

        var activePrescriptions = await _db.Prescriptions
            .CountAsync(p => p.PatientUserId == user.Id && p.Status == PrescriptionStatus.Active);

        var totalEncounters = await _db.ClinicalEncounters
            .CountAsync(e => e.PatientUserId == user.Id);

        var dependentsCount = await _db.DependentProfiles
            .CountAsync(d => d.GuardianUserId == user.Id);

        var activeDepId = HttpContext.Session.GetInt32(ProxyController.SessionActiveDependentId);
        var activeDepName = HttpContext.Session.GetString(ProxyController.SessionActiveDependentName);

        var model = new PatientDashboardViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ContactNumber = user.ContactNumber,
            CreatedAt = user.CreatedAt,
            NextAppointment = nextAppointment,
            HasTriageForNextAppointment = hasTriage,
            LabResults = latestLab,
            UnreadMessages = unreadMessages,
            ActivePrescriptionsCount = activePrescriptions,
            ClinicalEncountersCount = totalEncounters,
            DependentsCount = dependentsCount,
            ActiveDependentId = activeDepId,
            ActiveDependentName = activeDepName,
            Departments = ClinicalDepartments.All,
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
    public int UnreadMessages { get; set; }
    public int ActivePrescriptionsCount { get; set; }
    public int ClinicalEncountersCount { get; set; }
    public int DependentsCount { get; set; }
    public int? ActiveDependentId { get; set; }
    public string? ActiveDependentName { get; set; }
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}
