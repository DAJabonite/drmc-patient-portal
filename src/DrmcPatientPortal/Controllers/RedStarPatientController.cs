using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

[Authorize, AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Patient/RedStar")]
public sealed class RedStarPatientController(ApplicationDbContext db, RedStarPatientService patients, AdminWrites writes) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("")]
    public async Task<IActionResult> Index(int historyPage = 1, CancellationToken token = default)
    {
        var model = await patients.PageAsync(Actor, historyPage, token);
        return model is null ? NotFound() : View("~/Views/RedStar/Index.cshtml", model);
    }

    [HttpPost("Confirm/{id:int}")]
    public async Task<IActionResult> Confirm(int id, string? rowVersion, CancellationToken token)
    {
        if (!await (await ConfirmableAppointmentsAsync(db, id, token)).AnyAsync(token)) return NotFound();
        var version = AdminAudit.Version(rowVersion);
        if (version is null) return BadRequest();
        var result = await writes.RunAsync(async database =>
        {
            var appointment = await (await ConfirmableAppointmentsAsync(database, id, token)).AsTracking().SingleOrDefaultAsync(token);
            if (appointment is null) return WriteResult.Invalid("The appointment is no longer available.");
            if (!version.SequenceEqual(appointment.RowVersion)) return WriteResult.Stale;
            appointment.ConfirmedUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(token);
            await RedStarAuditTrail.RecordAsync(database, Actor, "ConfirmAttendance", "Appointment/" + id, token);
            return WriteResult.Success;
        }, token);
        return Finished(result);
    }

    [HttpPost("RequestReschedule/{id:int}")]
    public async Task<IActionResult> RequestReschedule(int id, RedStarRescheduleInput input, CancellationToken token)
    {
        if (!await OwnedAppointments(id).AnyAsync(token)) return NotFound();
        if (!ModelState.IsValid || input.RequestedDate.Date < ClinicalClock.Today || input.RequestedDate.Date > ClinicalClock.Today.AddYears(1)) return BadRequest();
        var version = AdminAudit.Version(input.RowVersion);
        if (version is null) return BadRequest();
        var result = await writes.RunAsync(async database =>
        {
            var appointment = await OwnedAppointments(id, database).SingleOrDefaultAsync(token);
            if (appointment is null) return WriteResult.Invalid("The appointment is no longer available.");
            if (!version.SequenceEqual(appointment.RowVersion)) return WriteResult.Stale;
            if (await database.RedStarRescheduleRequests.AnyAsync(r => r.AppointmentId == id && r.State == RedStarRequestState.Pending, token))
                return WriteResult.Invalid("A date change is already awaiting clinic review.");
            database.RedStarRescheduleRequests.Add(new() { AppointmentId = id, AppointmentRevision = appointment.Revision,
                RequestedDate = input.RequestedDate.Date, Reason = input.Reason?.Trim(), CreatedUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync(token);
            await RedStarAuditTrail.RecordAsync(database, Actor, "RequestReschedule", "Appointment/" + id, token);
            return WriteResult.Success;
        }, token);
        return Finished(result);
    }

    [HttpPost("Preferences")]
    public async Task<IActionResult> Preferences(bool emailEnabled, string? rowVersion, CancellationToken token)
    {
        var patientId = await patients.OwnedPatients(Actor).Select(p => (int?)p.Id).SingleOrDefaultAsync(token);
        if (patientId is null) return NotFound();
        var version = AdminAudit.Version(rowVersion);
        if (version is null) return BadRequest();
        var result = await writes.RunAsync(async database =>
        {
            var patient = await RedStarAccountAccess.OwnedPatients(database, Actor).SingleOrDefaultAsync(p => p.Id == patientId, token);
            if (patient is null) return WriteResult.Invalid("The appointment is no longer available.");
            if (!version.SequenceEqual(patient.RowVersion)) return WriteResult.Stale;
            patient.EmailRemindersEnabled = emailEnabled;
            var accountIndex = database.Phi.AccountIndex(Actor);
            if (!emailEnabled)
                await database.RedStarReminders.Where(r => r.PortalUserIdBlindIndex == accountIndex && r.Channel == RedStarReminderChannel.Email &&
                    (r.State == RedStarDeliveryState.Pending || r.State == RedStarDeliveryState.Sending))
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Cancelled)
                        .SetProperty(r => r.ProtectedPayload, (byte[]?)null).SetProperty(r => r.LeaseOwner, (string?)null)
                        .SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);
            await database.SaveChangesAsync(token);
            await RedStarAuditTrail.RecordAsync(database, Actor, "SetReminderPreference", "Patient/" + patientId, token);
            return WriteResult.Success;
        }, token);
        return Finished(result);
    }

    [HttpPost("Read/{id:guid}")]
    public async Task<IActionResult> Read(Guid id, CancellationToken token)
    {
        var ownedIds = patients.OwnedPatients(Actor).Select(p => p.Id);
        var accountIndex = db.Phi.AccountIndex(Actor);
        var prescriptions = await PrescriptionQueries.OwnedAsync(db, Actor, token);
        var refills = PrescriptionRefills.EligibleQuery(db).Where(a => prescriptions.Select(p => p.Id).Contains(a.PrescriptionId!.Value));
        var reminder = await db.RedStarReminders.Where(r => r.Id == id && r.PortalUserIdBlindIndex == accountIndex &&
            ownedIds.Contains(r.Appointment.Enrollment.PatientId) && r.Channel == RedStarReminderChannel.InApp &&
            r.State == RedStarDeliveryState.Delivered && r.AppointmentRevision == r.Appointment.Revision &&
            refills.Select(a => a.Id).Contains(r.AppointmentId)).AsTracking().SingleOrDefaultAsync(token);
        if (reminder is null) return NotFound();
        reminder.IsRead = true;
        await RedStarAuditTrail.RecordAsync(db, Actor, "ReadReminder", "Notification/" + id, token);
        return RedirectToAction(nameof(Index));
    }

    private async Task<IQueryable<RedStarAppointment>> ConfirmableAppointmentsAsync(ApplicationDbContext database, int id, CancellationToken token)
    {
        var prescriptions = await PrescriptionQueries.OwnedAsync(database, Actor, token);
        var refills = PrescriptionRefills.EligibleQuery(database).Where(a => prescriptions.Select(p => p.Id).Contains(a.PrescriptionId!.Value));
        return OwnedAppointments(id, database, includeRefills: true).Where(a =>
            a.Kind != RedStarAppointmentKind.Refill || refills.Select(r => r.Id).Contains(a.Id));
    }

    private IQueryable<RedStarAppointment> OwnedAppointments(int id, ApplicationDbContext? database = null, bool includeRefills = false)
    {
        database ??= db;
        var ownedIds = RedStarAccountAccess.OwnedPatients(database, Actor).Select(p => p.Id);
        return database.RedStarAppointments.Where(a => a.Id == id && ownedIds.Contains(a.Enrollment.PatientId) &&
            (includeRefills || a.Kind != RedStarAppointmentKind.Refill) && a.State == RedStarAppointmentState.Scheduled && a.Enrollment.Status == RedStarStatus.Active);
    }

    private IActionResult Finished(WriteResult result)
    {
        if (!result.Succeeded) return StatusCode(result.Conflict ? 409 : 400, result.Error);
        TempData["RedStarSaved"] = true;
        return RedirectToAction(nameof(Index));
    }
}

public sealed class RedStarRescheduleInput
{
    [Required] public string RowVersion { get; set; } = "";
    public DateTime RequestedDate { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}
