using System.Security.Claims;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed record RedStarRefillCard(int PrescriptionId, DateTime DueDate, bool Confirmed);
public sealed record RedStarPortalState(bool Enrolled, int UnreadCount);
public sealed record RedStarPatientPage(RedStarPatient Patient, IReadOnlyList<RedStarAppointment> Appointments,
    IReadOnlyList<Prescription> Prescriptions, IReadOnlyList<RedStarRescheduleRequest> Requests,
    IReadOnlyList<RedStarReminder> Notifications, RedStarRefillCard? NextRefill, DateTime Today,
    int HistoryPage, int HistoryTotal);

public sealed class RedStarPatientService(ApplicationDbContext db, IHttpContextAccessor http)
{
    private Task<RedStarPortalState>? portalState;
    public IQueryable<RedStarPatient> OwnedPatients(string userId) => RedStarAccountAccess.OwnedPatients(db, userId);

    public Task<RedStarPortalState> PortalStateAsync() => portalState ??= LoadStateAsync();
    private async Task<RedStarPortalState> LoadStateAsync()
    {
        var context = http.HttpContext;
        if (context?.Request.Path.StartsWithSegments("/Home/ServiceUnavailable") == true) return new(false, 0);
        var userId = context?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return new(false, 0);
        var owned = OwnedPatients(userId);
        var accountIndex = db.Phi.AccountIndex(userId);
        var patientId = await SqlReadRetry.RunAsync(db, () => owned.Select(p => (int?)p.Id).SingleOrDefaultAsync());
        if (patientId is null) return new(false, 0);
        context!.Response.Headers.CacheControl = "no-store";
        var ownedPrescriptions = await PrescriptionQueries.OwnedAsync(db, userId);
        var refills = PrescriptionRefills.EligibleQuery(db).Where(a => ownedPrescriptions.Select(p => p.Id).Contains(a.PrescriptionId!.Value));
        var unread = await SqlReadRetry.RunAsync(db, () => db.RedStarReminders.CountAsync(r => r.PortalUserIdBlindIndex == accountIndex &&
            r.Channel == RedStarReminderChannel.InApp && r.State == RedStarDeliveryState.Delivered && !r.IsRead &&
            owned.Select(p => p.Id).Contains(r.Appointment.Enrollment.PatientId) &&
            r.Appointment.Revision == r.AppointmentRevision && r.Appointment.State == RedStarAppointmentState.Scheduled &&
            refills.Select(a => a.Id).Contains(r.AppointmentId)));
        if (!await owned.AnyAsync(p => p.Id == patientId)) return new(false, 0);
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewPortalNavigation", "Patient/" + patientId);
        return new(true, unread);
    }

    public async Task<RedStarRefillCard?> NextRefillAsync(string userId, CancellationToken token = default)
    {
        var ownedIds = OwnedPatients(userId).Select(p => p.Id);
        var prescriptions = await PrescriptionQueries.OwnedAsync(db, userId, token);
        var card = await SqlReadRetry.RunAsync(db, () => PrescriptionRefills.EligibleQuery(db).AsNoTracking()
            .Where(a => ownedIds.Contains(a.Enrollment.PatientId) && prescriptions.Select(p => p.Id).Contains(a.PrescriptionId!.Value))
            .OrderBy(a => a.DueDate).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.DueDate, a.ConfirmedUtc, a.PrescriptionId }).FirstOrDefaultAsync(token), token);
        if (card is null) return null;
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewRefill", "Appointment/" + card.Id, token);
        return new(card.PrescriptionId!.Value, card.DueDate, card.ConfirmedUtc is not null);
    }

    public async Task<RedStarPatientPage?> PageAsync(string userId, int page, CancellationToken token)
    {
        var accountIndex = db.Phi.AccountIndex(userId);
        var patient = await SqlReadRetry.RunAsync(db, () => OwnedPatients(userId).AsNoTracking().Include(p => p.Enrollments).SingleOrDefaultAsync(token), token);
        if (patient is null) return null;
        var ownedIds = OwnedPatients(userId).Select(p => p.Id);
        var historyQuery = (await PrescriptionQueries.OwnedAsync(db, userId, token)).Where(p => p.IsRedStar && p.RedStarEnrollment!.PatientId == patient.Id);
        var refills = PrescriptionRefills.EligibleQuery(db).Where(a => historyQuery.Select(p => p.Id).Contains(a.PrescriptionId!.Value));
        var appointments = await db.RedStarAppointments.AsNoTracking().Include(a => a.Enrollment)
            .Where(a => a.Enrollment.PatientId == patient.Id && ownedIds.Contains(a.Enrollment.PatientId) &&
                a.State == RedStarAppointmentState.Scheduled && a.Enrollment.Status == RedStarStatus.Active &&
                (a.Kind != RedStarAppointmentKind.Refill || refills.Select(r => r.Id).Contains(a.Id)))
            .OrderBy(a => a.DueDate).ThenBy(a => a.Id).Take(100).ToListAsync(token);
        var total = await historyQuery.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var history = await historyQuery.OrderByDescending(p => p.PrescribedAt).ThenByDescending(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        var requests = await db.RedStarRescheduleRequests.AsNoTracking().Where(r => r.Appointment.Enrollment.PatientId == patient.Id && ownedIds.Contains(r.Appointment.Enrollment.PatientId))
            .Where(r => r.Appointment.Kind != RedStarAppointmentKind.Refill).OrderByDescending(r => r.CreatedUtc).Take(25).ToListAsync(token);
        var notifications = await db.RedStarReminders.AsNoTracking().Include(r => r.Appointment)
            .Where(r => r.PortalUserIdBlindIndex == accountIndex && r.Appointment.Enrollment.PatientId == patient.Id && ownedIds.Contains(r.Appointment.Enrollment.PatientId) && r.Channel == RedStarReminderChannel.InApp &&
                r.State == RedStarDeliveryState.Delivered && r.Appointment.Revision == r.AppointmentRevision &&
                refills.Select(a => a.Id).Contains(r.AppointmentId))
            .OrderByDescending(r => r.CreatedUtc).Take(25).ToListAsync(token);
        var nextRefill = await NextRefillAsync(userId, token);
        if (!await OwnedPatients(userId).AnyAsync(p => p.Id == patient.Id, token)) return null;
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewPatientSchedule", "Patient/" + patient.Id, token);
        return new(patient, appointments, history, requests, notifications, nextRefill, ClinicalClock.Today, page, total);
    }
}
