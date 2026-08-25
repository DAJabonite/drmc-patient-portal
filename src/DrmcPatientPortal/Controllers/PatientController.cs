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

        var nextAppointment = await _db.NextAppointments
            .Where(a => a.PatientUserId == user.Id)
            .OrderBy(a => a.ScheduledAt)
            .FirstOrDefaultAsync();

        var latestLab = await _db.LabResults
            .Where(l => l.PatientUserId == user.Id)
            .OrderByDescending(l => l.CollectedAt)
            .ToListAsync();

        var unreadMessages = await _db.Messages
            .CountAsync(m => m.PatientUserId == user.Id && !m.IsRead);

        var model = new PatientDashboardViewModel
        {
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            ContactNumber = user.ContactNumber,
            CreatedAt = user.CreatedAt,
            NextAppointment = nextAppointment,
            LabResults = latestLab,
            UnreadMessages = unreadMessages,
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
    public NextAppointment? NextAppointment { get; set; }
    public IReadOnlyList<LabResult> LabResults { get; set; } = Array.Empty<LabResult>();
    public int UnreadMessages { get; set; }
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}
