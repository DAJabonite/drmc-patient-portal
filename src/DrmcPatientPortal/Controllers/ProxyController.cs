using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// SECURITY REVIEW TODO (Item #2): Caregiver & Proxy Access Trust Model
// BOUNDARY NOTE: Guardian linking and in-session profile switching are fully built in code.
// Production hardening requirements:
// 1. Formal identity verification & PSA birth certificate validation before proxy activation.
// 2. Automated access expiration or re-consent when minor dependents reach age of majority (18 years old in the Philippines).
// 3. Strict logging and separation of proxy vs primary account actions.
// Reference: docs/SECURITY_REVIEW_TODO.md
[Authorize]
[Route("Patient/Proxy")]
public class ProxyController : Controller
{
    public const string SessionActiveDependentId = "ActiveDependentId";
    public const string SessionActiveDependentName = "ActiveDependentName";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;

    public ProxyController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    // GET /Patient/Proxy
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var dependents = await _db.DependentProfiles
            .Where(d => d.GuardianUserId == user.Id)
            .OrderBy(d => d.DateOfBirth)
            .ToListAsync();

        var activeDepId = HttpContext.Session.GetInt32(SessionActiveDependentId);
        var activeDepName = HttpContext.Session.GetString(SessionActiveDependentName);

        var model = new ProxyIndexViewModel
        {
            GuardianName = user.FullName,
            ActiveDependentId = activeDepId,
            ActiveDependentName = activeDepName,
            Dependents = dependents
        };

        return View(model);
    }

    // GET /Patient/Proxy/Add
    [HttpGet("Add")]
    public IActionResult Add()
    {
        return View(new AddDependentViewModel());
    }

    // POST /Patient/Proxy/Add
    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddDependentViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!model.StatutoryConsentAgreed)
        {
            ModelState.AddModelError(nameof(model.StatutoryConsentAgreed), "You must agree to the statutory caregiver declaration and data privacy terms.");
            return View(model);
        }

        var profile = new DependentProfile
        {
            GuardianUserId = user.Id,
            FullName = model.FullName.Trim(),
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            Relationship = model.Relationship,
            PhilHealthNumber = model.PhilHealthNumber?.Trim(),
            IdType = model.IdType?.Trim(),
            IdNumber = model.IdNumber?.Trim(),
            StatutoryConsentAgreed = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.DependentProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "ADD_DEPENDENT_PROFILE", $"DependentProfile/{profile.Id}", $"Added dependent profile for {profile.FullName} ({profile.Relationship})", ip);

        TempData["SuccessMessage"] = $"Dependent profile for {profile.FullName} was registered successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Patient/Proxy/SwitchProfile/{id}
    [HttpPost("SwitchProfile/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchProfile(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var dependent = await _db.DependentProfiles
            .FirstOrDefaultAsync(d => d.Id == id && d.GuardianUserId == user.Id);

        if (dependent is null)
        {
            return NotFound();
        }

        HttpContext.Session.SetInt32(SessionActiveDependentId, dependent.Id);
        HttpContext.Session.SetString(SessionActiveDependentName, dependent.FullName);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "PROXY_SWITCH", $"DependentProfile/{id}", $"Switched portal view context to dependent {dependent.FullName}", ip);

        TempData["SuccessMessage"] = $"You are now managing health records for {dependent.FullName}.";
        return RedirectToAction("Home", "Patient");
    }

    // POST /Patient/Proxy/SwitchToSelf
    [HttpPost("SwitchToSelf")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchToSelf()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        HttpContext.Session.Remove(SessionActiveDependentId);
        HttpContext.Session.Remove(SessionActiveDependentName);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "PROXY_SWITCH_SELF", $"ApplicationUser/{user.Id}", "Switched context back to primary guardian account.", ip);

        TempData["SuccessMessage"] = "Switched back to your personal health record.";
        return RedirectToAction("Home", "Patient");
    }
}

public class ProxyIndexViewModel
{
    public string GuardianName { get; set; } = string.Empty;
    public int? ActiveDependentId { get; set; }
    public string? ActiveDependentName { get; set; }
    public IReadOnlyList<DependentProfile> Dependents { get; set; } = Array.Empty<DependentProfile>();
}

public class AddDependentViewModel
{
    public string FullName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; } = DateTime.UtcNow.AddYears(-5);
    public string Gender { get; set; } = "Male";
    public RelationshipType Relationship { get; set; } = RelationshipType.Child;
    public string? PhilHealthNumber { get; set; }
    public string? IdType { get; set; } = "PSA Birth Certificate";
    public string? IdNumber { get; set; }
    public bool StatutoryConsentAgreed { get; set; } = true;
}
