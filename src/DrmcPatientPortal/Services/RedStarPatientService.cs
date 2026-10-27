using System.Security.Claims;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed record RedStarRefillCard(int AppointmentId, DateTime DueDate, string Medicines, bool Confirmed, string RowVersion,
    bool Verified = true);
public sealed record RedStarPortalState(bool Enrolled, int UnreadCount);
public sealed record RedStarPatientPage(RedStarPatient Patient, IReadOnlyList<RedStarAppointment> Appointments,
    IReadOnlyList<RedStarDispense> History, IReadOnlyList<RedStarRescheduleRequest> Requests,
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
        var unread = await SqlReadRetry.RunAsync(db, () => db.RedStarReminders.CountAsync(r => r.PortalUserIdBlindIndex == accountIndex &&
            r.Channel == RedStarReminderChannel.InApp && r.State == RedStarDeliveryState.Delivered && !r.IsRead &&
            owned.Select(p => p.Id).Contains(r.Appointment.Enrollment.PatientId) &&
            r.Appointment.Revision == r.AppointmentRevision && r.Appointment.State == RedStarAppointmentState.Scheduled &&
            r.Appointment.Enrollment.Status == RedStarStatus.Active));
        if (!await owned.AnyAsync(p => p.Id == patientId)) return new(false, 0);
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewPortalNavigation", "Patient/" + patientId);
        return new(true, unread);
    }

    public async Task<RedStarRefillCard?> NextRefillAsync(string userId, CancellationToken token = default)
    {
        var ownedIds = OwnedPatients(userId).Select(p => p.Id);
        var card = await SqlReadRetry.RunAsync(db, () => db.RedStarAppointments.AsNoTracking()
            .Where(a => ownedIds.Contains(a.Enrollment.PatientId) &&
                a.Kind == RedStarAppointmentKind.Refill && a.State == RedStarAppointmentState.Scheduled && a.Enrollment.Status == RedStarStatus.Active)
            .OrderBy(a => a.DueDate).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.DueDate, a.Enrollment.Medicines, a.ConfirmedUtc, a.RowVersion, a.RemindersVerified }).FirstOrDefaultAsync(token), token);
        if (card is null) return null;
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewRefill", "Appointment/" + card.Id, token);
        return new(card.Id, card.DueDate, card.Medicines, card.ConfirmedUtc is not null, Convert.ToBase64String(card.RowVersion), card.RemindersVerified);
    }

    public async Task<RedStarPatientPage?> PageAsync(string userId, int page, CancellationToken token)
    {
        var accountIndex = db.Phi.AccountIndex(userId);
        var patient = await SqlReadRetry.RunAsync(db, () => OwnedPatients(userId).AsNoTracking().Include(p => p.Enrollments).SingleOrDefaultAsync(token), token);
        if (patient is null) return null;
        var ownedIds = OwnedPatients(userId).Select(p => p.Id);
        var appointments = await db.RedStarAppointments.AsNoTracking().Include(a => a.Enrollment)
            .Where(a => a.Enrollment.PatientId == patient.Id && ownedIds.Contains(a.Enrollment.PatientId) && a.State == RedStarAppointmentState.Scheduled && a.Enrollment.Status == RedStarStatus.Active)
            .OrderBy(a => a.DueDate).ThenBy(a => a.Id).Take(100).ToListAsync(token);
        var historyQuery = db.RedStarDispenses.AsNoTracking().Where(d => d.Enrollment.PatientId == patient.Id && ownedIds.Contains(d.Enrollment.PatientId));
        var total = await historyQuery.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var history = await historyQuery.OrderByDescending(d => d.DispensedDate).ThenByDescending(d => d.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        var requests = await db.RedStarRescheduleRequests.AsNoTracking().Where(r => r.Appointment.Enrollment.PatientId == patient.Id && ownedIds.Contains(r.Appointment.Enrollment.PatientId))
            .OrderByDescending(r => r.CreatedUtc).Take(25).ToListAsync(token);
        var notifications = await db.RedStarReminders.AsNoTracking().Include(r => r.Appointment)
            .Where(r => r.PortalUserIdBlindIndex == accountIndex && r.Appointment.Enrollment.PatientId == patient.Id && ownedIds.Contains(r.Appointment.Enrollment.PatientId) && r.Channel == RedStarReminderChannel.InApp &&
                r.State == RedStarDeliveryState.Delivered && r.Appointment.Revision == r.AppointmentRevision &&
                r.Appointment.State == RedStarAppointmentState.Scheduled && r.Appointment.Enrollment.Status == RedStarStatus.Active)
            .OrderByDescending(r => r.CreatedUtc).Take(25).ToListAsync(token);
        var nextRefill = await NextRefillAsync(userId, token);
        if (!await OwnedPatients(userId).AnyAsync(p => p.Id == patient.Id, token)) return null;
        await RedStarAuditTrail.RecordAsync(db, userId, "ViewPatientSchedule", "Patient/" + patient.Id, token);
        return new(patient, appointments, history, requests, notifications, nextRefill, ClinicalClock.Today, page, total);
    }
}
