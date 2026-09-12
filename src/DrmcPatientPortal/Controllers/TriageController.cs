using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

// PRE-CONSULTATION SELF-TRIAGE & DIGITAL INTAKE
// BOUNDARY NOTE: Clinical intake responses, 0-10 visual pain scales, and acuity scores are saved to SQLite.
// Institutional clinician-workstation synchronization remains disabled until a DRMC integration is configured.
// A clinician-side workstation dashboard is out-of-scope for this patient-only portal build.
[Authorize]
[Route("Patient/Triage")]
public class TriageController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<TriageController> _logger;

    public TriageController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLog,
        ILogger<TriageController> logger)
    {
        _db = db;
        _userManager = userManager;
        _auditLog = auditLog;
        _logger = logger;
    }

    // GET /Patient/Triage/Start/{appointmentId}
    [HttpGet("Start/{appointmentId:int}")]
    public async Task<IActionResult> Start(int appointmentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientUserId == user.Id);

        if (appointment is null)
        {
            return NotFound();
        }

        // Check if an intake was already submitted
        var existing = await _db.TriageIntakes
            .FirstOrDefaultAsync(t => t.AppointmentId == appointmentId);

        if (existing is not null)
        {
            return RedirectToAction(nameof(Summary), new { id = existing.Id });
        }

        var model = new TriageSubmissionViewModel
        {
            AppointmentId = appointmentId,
            BookingReference = appointment.BookingReference,
            Department = appointment.Department,
            DoctorName = appointment.DoctorName,
            ScheduledAt = appointment.ScheduledAt,
            ChiefComplaint = appointment.ChiefComplaint
        };

        return View(model);
    }

    // POST /Patient/Triage/Submit
    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(TriageSubmissionViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == model.AppointmentId && a.PatientUserId == user.Id);

        if (appointment is null)
        {
            return NotFound();
        }

        var existing = await _db.TriageIntakes.FirstOrDefaultAsync(t => t.AppointmentId == appointment.Id);
        if (existing is not null) return RedirectToAction(nameof(Summary), new { id = existing.Id });

        if (!ModelState.IsValid)
        {
            model.BookingReference = appointment.BookingReference;
            model.Department = appointment.Department;
            model.DoctorName = appointment.DoctorName;
            model.ScheduledAt = appointment.ScheduledAt;
            return View("Start", model);
        }

        // Emergency red flag safeguard intercept
        if (model.HasChestPain || model.HasSevereBreathingDifficulty || model.HasSuddenNumbness || model.HasUncontrolledBleeding)
        {
            var redFlags = new List<string>();
            if (model.HasChestPain) redFlags.Add("Sudden crushing chest pain or pressure");
            if (model.HasSevereBreathingDifficulty) redFlags.Add("Severe breathing difficulty");
            if (model.HasSuddenNumbness) redFlags.Add("Sudden numbness or stroke warning signs");
            if (model.HasUncontrolledBleeding) redFlags.Add("Uncontrolled bleeding");
            var emergencyIntake = new TriageIntake
            {
                AppointmentId = appointment.Id,
                PatientUserId = user.Id,
                SubmittedAt = DateTime.UtcNow,
                ChiefComplaint = string.IsNullOrWhiteSpace(model.ChiefComplaint) ? appointment.ChiefComplaint : model.ChiefComplaint.Trim(),
                SymptomDurationDays = model.SymptomDurationDays,
                PainScale = model.PainScale,
                SymptomsJson = JsonSerializer.Serialize(redFlags),
                HasEmergencyRedFlags = true,
                ComorbiditiesJson = "[]",
                CurrentMedicationsSummary = model.CurrentMedicationsSummary ?? string.Empty,
                AcuityLevel = TriageAcuity.UrgentEmergency,
                TriageNotes = "Emergency warning displayed; patient instructed to seek immediate emergency care."
            };
            _db.TriageIntakes.Add(emergencyIntake);
            var ipRed = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            await PersistWithAuditAsync(emergencyIntake, user.Id, "EMERGENCY_RED_FLAG_TRIGGERED", "Emergency red-flag intake recorded.", ipRed);
            return RedirectToAction(nameof(EmergencyWarning));
        }

        var symptomsList = new List<string>();
        if (model.HasFever) symptomsList.Add("Fever / Chills");
        if (model.HasCough) symptomsList.Add("Persistent Cough");
        if (model.HasHeadache) symptomsList.Add("Headache / Dizziness");
        if (model.HasFatigue) symptomsList.Add("Fatigue / Weakness");
        if (model.HasGastrointestinal) symptomsList.Add("Nausea / Abdominal Discomfort");
        if (model.HasJointPain) symptomsList.Add("Joint or Muscle Pain");
        if (!string.IsNullOrWhiteSpace(model.OtherSymptoms)) symptomsList.Add(model.OtherSymptoms.Trim());

        var comorbiditiesList = new List<string>();
        if (model.HasHypertension) comorbiditiesList.Add("Hypertension");
        if (model.HasDiabetes) comorbiditiesList.Add("Diabetes Mellitus");
        if (model.HasAsthma) comorbiditiesList.Add("Asthma / COPD");
        if (model.HasHeartDisease) comorbiditiesList.Add("Cardiovascular Disease");
        if (model.HasKidneyDisease) comorbiditiesList.Add("Chronic Kidney Disease");

        // Determine Acuity
        var acuity = TriageAcuity.Routine;
        if (model.PainScale >= 7 || model.HasFever && model.SymptomDurationDays >= 7)
        {
            acuity = TriageAcuity.Priority;
        }

        var intake = new TriageIntake
        {
            AppointmentId = model.AppointmentId,
            PatientUserId = user.Id,
            SubmittedAt = DateTime.UtcNow,
            ChiefComplaint = string.IsNullOrWhiteSpace(model.ChiefComplaint) ? appointment.ChiefComplaint : model.ChiefComplaint,
            SymptomDurationDays = model.SymptomDurationDays,
            PainScale = model.PainScale,
            SymptomsJson = JsonSerializer.Serialize(symptomsList),
            HasEmergencyRedFlags = false,
            ReportedBloodPressure = model.ReportedBloodPressure,
            ReportedTemperature = model.ReportedTemperature,
            ReportedHeartRate = model.ReportedHeartRate,
            ReportedWeightKg = model.ReportedWeightKg,
            ReportedBloodSugar = model.ReportedBloodSugar,
            ComorbiditiesJson = JsonSerializer.Serialize(comorbiditiesList),
            CurrentMedicationsSummary = model.CurrentMedicationsSummary ?? string.Empty,
            AcuityLevel = acuity,
            TriageNotes = $"Digital pre-intake completed by patient. Acuity: {acuity}. Reported vitals and symptom checklist ready for triage verification."
        };

        _db.TriageIntakes.Add(intake);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        await PersistWithAuditAsync(intake, user.Id, "SUBMIT_TRIAGE_INTAKE", "Digital self-triage submitted.", ip);

        _logger.LogInformation("[TRIAGE_INTAKE_RECORDED] Intake {IntakeId} recorded locally with acuity {Acuity}.",
            intake.Id, intake.AcuityLevel);

        return RedirectToAction(nameof(Summary), new { id = intake.Id });
    }

    // GET /Patient/Triage/Summary/{id}
    [HttpGet("Summary/{id:int}")]
    public async Task<IActionResult> Summary(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var intake = await _db.TriageIntakes
            .Include(t => t.Appointment)
            .FirstOrDefaultAsync(t => t.Id == id && t.PatientUserId == user.Id);

        if (intake is null)
        {
            return NotFound();
        }

        return View(intake);
    }

    // GET /Patient/Triage/EmergencyWarning
    [HttpGet("EmergencyWarning")]
    public IActionResult EmergencyWarning()
    {
        return View();
    }

    private async Task PersistWithAuditAsync(TriageIntake intake, string userId, string action, string details, string ipAddress)
    {
        var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            await _db.SaveChangesAsync();
            await _auditLog.LogAsync(userId, action, $"TriageIntake/{intake.Id}", details, ipAddress);
            if (transaction is not null) await transaction.CommitAsync();
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }
}

public class TriageSubmissionViewModel
{
    public int AppointmentId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }

    [StringLength(500)]
    public string ChiefComplaint { get; set; } = string.Empty;
    [Range(1, 365)]
    public int SymptomDurationDays { get; set; } = 1;
    [Range(0, 10)]
    public int PainScale { get; set; } = 0; // 0 to 10

    // Symptoms checkboxes
    public bool HasFever { get; set; }
    public bool HasCough { get; set; }
    public bool HasHeadache { get; set; }
    public bool HasFatigue { get; set; }
    public bool HasGastrointestinal { get; set; }
    public bool HasJointPain { get; set; }
    [StringLength(300)]
    public string? OtherSymptoms { get; set; }

    // Red Flag safeguards
    public bool HasChestPain { get; set; }
    public bool HasSevereBreathingDifficulty { get; set; }
    public bool HasSuddenNumbness { get; set; }
    public bool HasUncontrolledBleeding { get; set; }

    // Vitals
    [StringLength(20)] public string? ReportedBloodPressure { get; set; }
    [StringLength(20)] public string? ReportedTemperature { get; set; }
    [StringLength(20)] public string? ReportedHeartRate { get; set; }
    [StringLength(20)] public string? ReportedWeightKg { get; set; }
    [StringLength(20)] public string? ReportedBloodSugar { get; set; }

    // Comorbidities
    public bool HasHypertension { get; set; }
    public bool HasDiabetes { get; set; }
    public bool HasAsthma { get; set; }
    public bool HasHeartDisease { get; set; }
    public bool HasKidneyDisease { get; set; }

    [StringLength(500)] public string? CurrentMedicationsSummary { get; set; }
}
