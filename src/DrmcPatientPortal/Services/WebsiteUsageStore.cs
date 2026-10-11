using System.Data;
using System.Security.Cryptography;
using System.Text;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class WebsiteUsageOptions
{
    public const string Section = "WebsiteUsage";
    public bool Enabled { get; set; }
}

public static class WebsiteUsageClock
{
    public static DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(ClinicalClock.WallTime(utc));
    public static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
    public static readonly TimeSpan GapTolerance = TimeSpan.FromMinutes(2);
}

// Retain a failed collection signal until a successful maintenance operation persists it.
// The atomic exchange lets requests continue marking newer failures during maintenance.
public sealed class WebsiteUsageHealth
{
    private long failureTicks;
    public void Failed(DateTimeOffset at) => Interlocked.CompareExchange(ref failureTicks, at.UtcTicks, 0);
    public DateTimeOffset? TakeFailure()
    {
        var ticks = Interlocked.Exchange(ref failureTicks, 0);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

public sealed class WebsiteUsageStore(ApplicationDbContext db, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("DRMC.WebsiteUsage.DailyKey.v1");

    // A SQL range/update lock also serializes creation of an absent day across app processes.
    // All marker checks and counter increments run under this lock in the same transaction.
    private async Task<WebsiteUsageDay> LockDayAsync(DateTimeOffset now, CancellationToken token)
    {
        var day = WebsiteUsageClock.Day(now);
        var row = await db.WebsiteUsageDays.FromSqlInterpolated(
            $"SELECT * FROM [WebsiteUsageDays] WITH (UPDLOCK, HOLDLOCK) WHERE [Day] = {day}")
            .SingleOrDefaultAsync(token);
        if (row is null)
        {
            row = new WebsiteUsageDay
            {
                Day = day, ProtectedKey = protector.Protect(RandomNumberGenerator.GetBytes(32)),
                CoverageStartedUtc = now, LastHeartbeatUtc = now,
                HasGap = now - WebsiteUsageClock.Start(day) > WebsiteUsageClock.GapTolerance,
            };
            db.WebsiteUsageDays.Add(row);
            await db.SaveChangesAsync(token);
        }
        if (now - row.LastHeartbeatUtc > WebsiteUsageClock.GapTolerance) row.HasGap = true;
        if (now > row.LastHeartbeatUtc) row.LastHeartbeatUtc = now;
        return row;
    }

    private static string Identifier(byte[] key, string prefix, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "\n" + value)));

    public async Task RecordAsync(DateTimeOffset now, string address, string userAgent, string? accountId, CancellationToken token)
    {
        if (accountId is not null && await (from membership in db.UserRoles
            join role in db.Roles on membership.RoleId equals role.Id
            where membership.UserId == accountId && StaffRoles.All.Contains(role.Name!)
            select membership.UserId).AnyAsync(token)) return;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var row = await LockDayAsync(now, token);
        var key = protector.Unprotect(row.ProtectedKey ?? throw new InvalidOperationException("The daily usage key has expired."));
        try
        {
            var visitor = Identifier(key, "visitor", address + "\n" + userAgent);
            var patient = accountId is null ? null : Identifier(key, "patient", accountId);
            var existing = await db.WebsiteUsageMarkers.Where(m => m.Day == row.Day &&
                (m.Audience == WebsiteUsageAudience.Visitor && m.Identifier == visitor ||
                 m.Audience == WebsiteUsageAudience.Patient && m.Identifier == patient)).ToListAsync(token);
            if (!existing.Any(m => m.Audience == WebsiteUsageAudience.Visitor))
            {
                db.WebsiteUsageMarkers.Add(new() { Day = row.Day, Audience = WebsiteUsageAudience.Visitor, Identifier = visitor });
                row.EstimatedVisitors++;
            }
            if (patient is not null && !existing.Any(m => m.Audience == WebsiteUsageAudience.Patient))
            {
                db.WebsiteUsageMarkers.Add(new() { Day = row.Day, Audience = WebsiteUsageAudience.Patient, Identifier = patient });
                row.ActivePatients++;
            }
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async Task MaintainAsync(DateTimeOffset now, bool enabled, DateTimeOffset? failure, CancellationToken token)
    {
        var today = WebsiteUsageClock.Day(now);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (enabled) await LockDayAsync(now, token);
        else await db.WebsiteUsageDays.Where(d => d.Day == today).ExecuteUpdateAsync(s => s.SetProperty(d => d.HasGap, true), token);
        if (failure.HasValue)
        {
            var failedDay = WebsiteUsageClock.Day(failure.Value);
            await db.WebsiteUsageDays.Where(d => d.Day >= failedDay && d.Day <= today)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.HasGap, true), token);
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        // Cleanup remains active when collection is disabled. Deletion is idempotent and salts
        // are never re-created for historical days. Shared-key caches are deliberately avoided.
        var expiredBefore = now >= WebsiteUsageClock.Start(today).AddMinutes(5) ? today : today.AddDays(-1);
        await db.WebsiteUsageMarkers.Where(m => m.Day < expiredBefore).ExecuteDeleteAsync(token);
        await db.WebsiteUsageDays.Where(d => d.Day < expiredBefore && d.ProtectedKey != null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ProtectedKey, (byte[]?)null), token);
        var retention = today.AddMonths(-13);
        await db.WebsiteUsageDays.Where(d => d.Day < retention).ExecuteDeleteAsync(token);
    }
}
