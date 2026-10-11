using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class RedStarAuditTrail
{
    public static async Task RecordAsync(ApplicationDbContext db, string actor, string action, string subject, CancellationToken token = default)
    {
        db.RedStarAudits.Add(new RedStarAudit { ActorId = actor, Action = action, Subject = subject, TimestampUtc = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(token); }
        catch (Exception error) when (error is not OperationCanceledException and not DbUpdateConcurrencyException)
        { throw new AuditLogPersistenceException("Private access audit could not be recorded.", error); }
    }

    public static Task CancelRemindersAsync(ApplicationDbContext db, IQueryable<RedStarAppointment> appointments, CancellationToken token = default) =>
        db.RedStarReminders.Where(r => appointments.Select(a => a.Id).Contains(r.AppointmentId) &&
            (r.State == RedStarDeliveryState.Pending || r.State == RedStarDeliveryState.Sending || r.Channel == RedStarReminderChannel.InApp))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarDeliveryState.Cancelled)
                .SetProperty(r => r.ProtectedPayload, (byte[]?)null).SetProperty(r => r.LeaseOwner, (string?)null)
                .SetProperty(r => r.LeaseExpiresUtc, (DateTimeOffset?)null), token);
}
