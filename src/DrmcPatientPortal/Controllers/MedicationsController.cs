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
    private readonly ILogger<MedicationsController> _logger;

    public MedicationsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        ILogger<MedicationsController> logger)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _logger = logger;
    }

    // GET /Patient/Medications
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var prescriptions = await _db.Prescriptions
            .Include(p => p.RefillRequests)
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
            ActiveCount = prescriptions.Count(p => p.Status == PrescriptionStatus.Active),
            PendingRefillsCount = prescriptions.SelectMany(p => p.RefillRequests).Count(r => r.Status == Models.RefillStatus.Requested || r.Status == Models.RefillStatus.ReadyForPickup)
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
            .Include(p => p.RefillRequests)
            .FirstOrDefaultAsync(p => p.Id == id && p.PatientUserId == user.Id);

        if (rx is null)
        {
            return NotFound();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "VIEW_PRESCRIPTION", $"Prescription/{id}", $"Rx #{rx.RxNumber} - {rx.GenericName}", ip);

        return View(rx);
    }

    // POST /Patient/Medications/RequestRefill
    [HttpPost("RequestRefill")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRefill(int prescriptionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var rx = await _db.Prescriptions
            .Include(p => p.RefillRequests)
            .FirstOrDefaultAsync(p => p.Id == prescriptionId && p.PatientUserId == user.Id);

        if (rx is null)
        {
            return NotFound();
        }

        if (rx.Status != PrescriptionStatus.Active || rx.RefillsRemaining <= 0)
        {
            TempData["ErrorMessage"] = "This prescription has no refills remaining or is not active. Please consult your physician.";
            return RedirectToAction(nameof(Details), new { id = prescriptionId });
        }

        // Check if there is already an active pending request
        var hasPending = rx.RefillRequests.Any(r => r.Status == Models.RefillStatus.Requested || r.Status == Models.RefillStatus.Approved);
        if (hasPending)
        {
            TempData["ErrorMessage"] = "A refill request is already pending processing for this medication.";
            return RedirectToAction(nameof(Details), new { id = prescriptionId });
        }

        // Create refill request and decrement remaining refills
        var refill = new RefillRequest
        {
            PrescriptionId = rx.Id,
            PatientUserId = user.Id,
            RequestedAt = DateTime.UtcNow,
            Status = Models.RefillStatus.Approved, // Pre-approved in hospital workflow
            EstimatedPickupDate = DateTime.UtcNow.AddDays(1),
            PharmacyNotes = "Refill approved. Prescription slip sent to DRMC OPD Pharmacy Window 2 for dispensing."
        };

        rx.RefillsRemaining--;
        rx.LastRefillDate = DateTime.UtcNow;
        rx.RefillRequests.Add(refill);

        await _db.SaveChangesAsync();

        // PHARMACY INFRASTRUCTURE BOUNDARY LOG
        _logger.LogInformation("[PHARMACY_REFILL_SUBMITTED] Rx #{RxNumber} ({Drug}) submitted for patient {UserId}. Refills remaining: {Remaining}",
            rx.RxNumber, rx.GenericName, user.Id, rx.RefillsRemaining);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await _auditLog.LogAsync(user.Id, "REQUEST_REFILL", $"Prescription/{rx.Id}", $"Refill request #{refill.Id} created for Rx {rx.RxNumber}", ip);

        TempData["SuccessMessage"] = $"Your refill request for {rx.GenericName} has been approved and sent to the DRMC OPD Pharmacy.";
        return RedirectToAction(nameof(RefillStatus), new { id = refill.Id });
    }

    // GET /Patient/Medications/RefillStatus/{id}
    [HttpGet("RefillStatus/{id:int}")]
    public async Task<IActionResult> RefillStatus(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var refill = await _db.RefillRequests
            .Include(r => r.Prescription)
            .FirstOrDefaultAsync(r => r.Id == id && r.PatientUserId == user.Id);

        if (refill is null)
        {
            return NotFound();
        }

        return View(refill);
    }
}

public class MedicationsIndexViewModel
{
    public IReadOnlyList<Prescription> Prescriptions { get; set; } = Array.Empty<Prescription>();
    public IReadOnlyList<PatientAllergy> Allergies { get; set; } = Array.Empty<PatientAllergy>();
    public int ActiveCount { get; set; }
    public int PendingRefillsCount { get; set; }
}
