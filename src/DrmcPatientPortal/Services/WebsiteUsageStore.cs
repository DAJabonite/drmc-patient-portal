using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class WebsiteUsageOptions
{
    public const string Section = "WebsiteUsage";
    public bool Enabled { get; set; }
    public int RetentionMonths { get; set; } = 13;
}

public static class WebsiteUsageClock
{
    public static DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(ClinicalClock.WallTime(utc));
    public static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
    public static readonly TimeSpan GapTolerance = TimeSpan.FromMinutes(2);

    public static DateOnly IdentifierCutoff(DateTimeOffset now)
    {
        var today = Day(now);
        return now >= Start(today).AddMinutes(5) ? today : today.AddDays(-1);
    }

    public static DateTimeOffset NextCleanup(DateTimeOffset now)
    {
        var cleanup = Start(Day(now)).AddMinutes(5);
        return now < cleanup ? cleanup : cleanup.AddDays(1);
    }
}

// Retain a failed collection signal until a successful maintenance operation persists it.
public sealed class WebsiteUsageHealth
{
    private long failureTicks;
    public void Failed(DateTimeOffset at)
    {
        var previous = Volatile.Read(ref failureTicks);
        while (previous == 0 || at.UtcTicks < previous)
        {
            var observed = Interlocked.CompareExchange(ref failureTicks, at.UtcTicks, previous);
            if (observed == previous) return;
            previous = observed;
        }
    }

    public DateTimeOffset? TakeFailure()
    {
        var ticks = Interlocked.Exchange(ref failureTicks, 0);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}

public sealed class WebsiteUsageStore
{
    public const int Capacity = 2_000;
    public const int BatchSize = 250;
    public const int PruneBatchSize = 1_000;
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory scopes;
    private readonly IDataProtector protector;
    private readonly TimeProvider clock;
    private readonly WebsiteUsageHealth health;
    private readonly int retentionMonths;
    private readonly Channel<WebsiteUsageEvent> events;
    private readonly Channel<bool> ready = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite, AllowSynchronousContinuations = false,
    });
    private readonly SemaphoreSlim consumer = new(1, 1);
    // A failed/cancelled batch is kept separately until commit; never an unbounded retry queue.
    private List<WebsiteUsageEvent>? pending;

    public WebsiteUsageStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider clock,
        IOptions<WebsiteUsageOptions> options, WebsiteUsageHealth health)
    {
        this.scopes = scopes;
        this.clock = clock;
        this.health = health;
        retentionMonths = options.Value.RetentionMonths;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionMonths);
        protector = protection.CreateProtector("DRMC.WebsiteUsage.DailyKey.v1");
        events = Channel.CreateBounded<WebsiteUsageEvent>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest, AllowSynchronousContinuations = false,
            SingleReader = true, SingleWriter = false,
        }, dropped => health.Failed(dropped.OccurredUtc));
    }

    // Request path: no scope, SQL, crypto, consumer lock, or asynchronous continuation.
    public Task RecordAsync(DateTimeOffset now, string address, string userAgent, string? accountId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!events.Writer.TryWrite(new(now, address, userAgent, accountId))) health.Failed(now);
        else if (events.Reader.Count >= BatchSize) ready.Writer.TryWrite(true);
        return Task.CompletedTask;
    }

    public void Complete() => events.Writer.TryComplete();

    internal async Task WaitForFlushAsync(CancellationToken token)
    {
        if (events.Reader.Count >= BatchSize) return;
        using var deadline = new CancellationTokenSource(FlushInterval, clock);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        try { await ready.Reader.ReadAsync(wait.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        // Coalesce notifications; producers never run the consumer inline.
        while (ready.Reader.TryRead(out _)) { }
    }

    /// <summary>
    /// Persists the pending batch and the queue snapshot after acquiring the consumer lock.
    /// Later arrivals cannot extend the drain indefinitely. Failures propagate and retain the
    /// unsuccessful batch for the next call; overflow/expired events remain best effort.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await consumer.WaitAsync(cancellationToken);
        try
        {
            var remaining = events.Reader.Count + (pending?.Count ?? 0);
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pending is null)
                {
                    pending = new(BatchSize);
                    while (pending.Count < Math.Min(BatchSize, remaining) && events.Reader.TryRead(out var item)) pending.Add(item);
                }
                if (pending.Count == 0) { pending = null; break; }
                remaining -= pending.Count;
                var failureAt = pending.Min(e => e.OccurredUtc);
                try { await PersistAsync(pending, cancellationToken); }
                catch
                {
                    health.Failed(failureAt);
                    throw;
                }
                pending = null;
            }
        }
        finally { consumer.Release(); }
    }

    private static string Identifier(byte[] key, string prefix, string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(prefix + "\n" + value)));

    // SQL range/update locks serialize absent-day creation across app processes. No save here:
    // the day, markers and counter changes are written together at the end of the batch.
    private static Task<WebsiteUsageDay?> FindDayAsync(ApplicationDbContext db, DateOnly day, CancellationToken token) =>
        db.WebsiteUsageDays.FromSqlInterpolated(
            $"SELECT * FROM [WebsiteUsageDays] WITH (UPDLOCK, HOLDLOCK) WHERE [Day] = {day}").SingleOrDefaultAsync(token);

    private WebsiteUsageDay CreateDay(ApplicationDbContext db, DateTimeOffset now)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var day = WebsiteUsageClock.Day(now);
            var row = new WebsiteUsageDay
            {
                Day = day, ProtectedKey = protector.Protect(key), CoverageStartedUtc = now, LastHeartbeatUtc = now,
                HasGap = now - WebsiteUsageClock.Start(day) > WebsiteUsageClock.GapTolerance,
            };
            db.WebsiteUsageDays.Add(row);
            return row;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void Heartbeat(WebsiteUsageDay row, DateTimeOffset now)
    {
        if (now - row.LastHeartbeatUtc > WebsiteUsageClock.GapTolerance) row.HasGap = true;
        if (now > row.LastHeartbeatUtc) row.LastHeartbeatUtc = now;
    }

    private async Task PersistAsync(List<WebsiteUsageEvent> batch, CancellationToken token)
    {
        var cutoff = WebsiteUsageClock.IdentifierCutoff(clock.GetUtcNow());
        var oldest = batch.Min(e => e.OccurredUtc);
        if (batch.RemoveAll(e => WebsiteUsageClock.Day(e.OccurredUtc) < cutoff) > 0) health.Failed(oldest);
        var eligible = batch.ToArray();
        if (eligible.Length == 0) return;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var accounts = eligible.Where(e => e.AccountId is not null).Select(e => e.AccountId!).Distinct().ToArray();
        var staff = accounts.Length == 0 ? new HashSet<string>() : (await (from membership in db.UserRoles
            join role in db.Roles on membership.RoleId equals role.Id
            where accounts.Contains(membership.UserId) && StaffRoles.All.Contains(role.Name!)
            select membership.UserId).Distinct().ToListAsync(token)).ToHashSet();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        foreach (var group in eligible.Where(e => e.AccountId is null || !staff.Contains(e.AccountId))
            .GroupBy(e => WebsiteUsageClock.Day(e.OccurredUtc)).OrderBy(g => g.Key))
        {
            var row = await FindDayAsync(db, group.Key, token);
            // Recheck after waiting for SQL: another process may have purged this day's key.
            if (group.Key < WebsiteUsageClock.IdentifierCutoff(clock.GetUtcNow()) || row is { ProtectedKey: null })
            {
                health.Failed(group.Min(e => e.OccurredUtc));
                continue;
            }
            row ??= CreateDay(db, group.Min(e => e.OccurredUtc));
            var key = protector.Unprotect(row.ProtectedKey!);
            try
            {
                var candidates = new HashSet<(WebsiteUsageAudience Audience, string Identifier)>();
                foreach (var item in group.OrderBy(e => e.OccurredUtc))
                {
                    Heartbeat(row, item.OccurredUtc);
                    candidates.Add((WebsiteUsageAudience.Visitor, Identifier(key, "visitor", item.Address + "\n" + item.UserAgent)));
                    if (item.AccountId is not null) candidates.Add((WebsiteUsageAudience.Patient, Identifier(key, "patient", item.AccountId)));
                }
                var identifiers = candidates.Select(m => m.Identifier).ToArray();
                var existing = await db.WebsiteUsageMarkers.Where(m => m.Day == group.Key && identifiers.Contains(m.Identifier))
                    .Select(m => new { m.Audience, m.Identifier }).ToListAsync(token);
                candidates.ExceptWith(existing.Select(m => (m.Audience, m.Identifier)));
                foreach (var marker in candidates)
                {
                    db.WebsiteUsageMarkers.Add(new() { Day = group.Key, Audience = marker.Audience, Identifier = marker.Identifier });
                    if (marker.Audience == WebsiteUsageAudience.Visitor) row.EstimatedVisitors++;
                    else row.ActivePatients++;
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task MaintainAsync(DateTimeOffset now, bool enabled, DateTimeOffset? failure, CancellationToken token)
    {
        await consumer.WaitAsync(token);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var today = WebsiteUsageClock.Day(now);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            // Apply bulk changes before loading the tracked heartbeat row, avoiding stale state.
            if (failure.HasValue)
            {
                var failedDay = WebsiteUsageClock.Day(failure.Value);
                await db.WebsiteUsageDays.Where(d => d.Day >= failedDay && d.Day <= today)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.HasGap, true), token);
            }
            if (enabled)
            {
                var row = await FindDayAsync(db, today, token) ?? CreateDay(db, now);
                Heartbeat(row, now);
                if (failure.HasValue && WebsiteUsageClock.Day(failure.Value) <= today) row.HasGap = true;
            }
            else await db.WebsiteUsageDays.Where(d => d.Day == today)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.HasGap, true), token);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        finally { consumer.Release(); }
    }

    public async Task PruneAsync(DateTimeOffset now, CancellationToken token = default)
    {
        var expiredBefore = WebsiteUsageClock.IdentifierCutoff(now);
        var today = WebsiteUsageClock.Day(now);
        var retention = retentionMonths >= (today.Year - 1) * 12 + today.Month
            ? DateOnly.MinValue : today.AddMonths(-retentionMonths);
        // Each chunk releases the consumer and SQL day lock. Expire the key in the same
        // transaction as its first marker chunk, fencing writers before further deletion.
        while (await PruneDayAsync(expiredBefore, token)) { }
        // All expired markers go first so aggregate deletion cannot cascade a large backlog.
        await PruneChunksAsync(db => db.WebsiteUsageDays.Where(d => d.Day < retention)
            .OrderBy(d => d.Day).Take(PruneBatchSize).ExecuteDeleteAsync(token), token);
    }

    private async Task<bool> PruneDayAsync(DateOnly expiredBefore, CancellationToken token)
    {
        await consumer.WaitAsync(token);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            // Also lock the absent-day range: a buffered writer may still be creating its day.
            // Acquiring the day lock before deleting markers preserves cross-process deduplication.
            var day = await db.WebsiteUsageDays.FromSqlRaw("SELECT * FROM [WebsiteUsageDays] WITH (UPDLOCK, HOLDLOCK)")
                .Where(d => d.Day < expiredBefore && (d.ProtectedKey != null || db.WebsiteUsageMarkers.Any(m => m.Day == d.Day)))
                .OrderBy(d => d.Day).Select(d => (DateOnly?)d.Day).FirstOrDefaultAsync(token);
            if (day is null) return false;
            await db.WebsiteUsageMarkers.Where(m => m.Day == day.Value)
                .OrderBy(m => m.Audience).ThenBy(m => m.Identifier).Take(PruneBatchSize).ExecuteDeleteAsync(token);
            await db.WebsiteUsageDays.Where(d => d.Day == day.Value && d.ProtectedKey != null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ProtectedKey, (byte[]?)null), token);
            await transaction.CommitAsync(token);
            return true;
        }
        finally { consumer.Release(); }
    }

    private async Task PruneChunksAsync(Func<ApplicationDbContext, Task<int>> prune, CancellationToken token)
    {
        int affected;
        do
        {
            await consumer.WaitAsync(token);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                affected = await prune(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            }
            finally { consumer.Release(); }
        } while (affected == PruneBatchSize);
    }
}
