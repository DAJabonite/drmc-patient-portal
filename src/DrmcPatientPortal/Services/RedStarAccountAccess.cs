using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

// Account IDs are decrypted only in the application. There is deliberately no
// database relationship between the private registry and Identity/shared care.
public static class RedStarAccountAccess
{
    public static IQueryable<ApplicationUser> EligibleAccounts(ApplicationDbContext db, DateTimeOffset now) =>
        db.Users.AsNoTracking().Where(u => u.EmailConfirmed && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now));

    public static IQueryable<RedStarPatient> OwnedPatients(ApplicationDbContext db, string userId)
    {
        var index = db.Phi.AccountIndex(userId);
        return db.RedStarPatients.Where(p => p.PortalUserIdBlindIndex == index &&
            db.Users.Any(u => u.Id == userId && u.EmailConfirmed &&
                (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow)));
    }

    private static void Clear(RedStarPatient patient)
    {
        patient.PortalUserId = null; patient.PortalUserIdBlindIndex = null; patient.EmailRemindersEnabled = false;
    }
    private static void Clear(RedStarReminder reminder)
    {
        reminder.PortalUserId = null; reminder.PortalUserIdBlindIndex = null; reminder.ProtectedPayload = null;
        reminder.State = RedStarDeliveryState.Cancelled; reminder.LeaseOwner = null; reminder.LeaseExpiresUtc = null;
    }
    public static void Detach(ApplicationDbContext db, string userId)
    {
        var index = db.Phi.AccountIndex(userId);
        foreach (var patient in db.RedStarPatients.Where(p => p.PortalUserIdBlindIndex == index).ToList()) Clear(patient);
        foreach (var reminder in db.RedStarReminders.Where(r => r.PortalUserIdBlindIndex == index).ToList()) Clear(reminder);
    }
    public static async Task DetachAsync(ApplicationDbContext db, string userId, CancellationToken token)
    {
        var index = db.Phi.AccountIndex(userId);
        foreach (var patient in await db.RedStarPatients.Where(p => p.PortalUserIdBlindIndex == index).ToListAsync(token)) Clear(patient);
        foreach (var reminder in await db.RedStarReminders.Where(r => r.PortalUserIdBlindIndex == index).ToListAsync(token)) Clear(reminder);
    }
    public static async Task ReconcileOrphansAsync(ApplicationDbContext db, CancellationToken token)
    {
        var after = 0;
        while (true)
        {
            var batch = await db.RedStarPatients.AsNoTracking().Where(p => p.Id > after && p.PortalUserIdBlindIndex != null)
                .OrderBy(p => p.Id).Take(100).Select(p => new { p.Id, p.PortalUserId }).ToListAsync(token);
            if (batch.Count == 0) break;
            after = batch[^1].Id;
            var ids = batch.Select(p => p.PortalUserId!).Distinct().ToArray();
            var existing = await db.Users.Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(token);
            foreach (var id in ids.Except(existing, StringComparer.Ordinal)) await DetachAsync(db, id, token);
            await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();
        }
        // Historical deliveries can outlive a reassigned patient binding.
        var reminderIds = await db.RedStarReminders.AsNoTracking().Where(r => r.PortalUserIdBlindIndex != null)
            .Select(r => r.Id).ToListAsync(token);
        foreach (var chunk in reminderIds.Chunk(100))
        {
            var userIds = (await db.RedStarReminders.AsNoTracking().Where(r => chunk.Contains(r.Id)).Select(r => r.PortalUserId!).ToListAsync(token))
                .Distinct(StringComparer.Ordinal).ToArray();
            var existing = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(token);
            foreach (var id in userIds.Except(existing, StringComparer.Ordinal)) await DetachAsync(db, id, token);
            await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();
        }
    }
}
