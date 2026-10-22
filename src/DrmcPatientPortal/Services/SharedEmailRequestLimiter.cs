using System.Data;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

// Unknown and registered recipients use the same SQL-backed limit. The key contains
// no address, is domain separated, and stays stable across replicas and restarts.
public sealed class SharedEmailRequestLimiter(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<PortalRateLimitOptions> options)
{
    public async Task<(bool Allowed, int RetryAfter)> AcquireAsync(string recipient, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var window = TimeSpan.FromMinutes(15);
        var start = new DateTimeOffset(now.UtcTicks / window.Ticks * window.Ticks, TimeSpan.Zero);
        var retryAfter = Math.Max(1, (int)Math.Ceiling((start + window - now).TotalSeconds));
        for (var attempt = 0; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var key = db.Phi.EmailRecipientIndex(recipient.Trim().ToUpperInvariant());
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                // Acquire an update range lock before reading. Concurrent first requests
                // otherwise take shared range locks and deadlock while inserting the row.
                var row = await db.AccountEmailThrottles.FromSqlInterpolated($"SELECT * FROM AccountEmailThrottles WITH (UPDLOCK, HOLDLOCK) WHERE RecipientHash = {key} AND WindowStartUtc = {start}")
                    .SingleOrDefaultAsync(token);
                if (row?.Attempts >= options.Value.EmailPerAccount) return (false, retryAfter);
                if (row is null) db.AccountEmailThrottles.Add(new AccountEmailThrottle { RecipientHash = key, WindowStartUtc = start, Attempts = 1 });
                else row.Attempts++;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return (true, retryAfter);
            }
            catch (Exception error) when (attempt < 2 &&
                error.GetBaseException() is SqlException { Number: 1205 or 2601 or 2627 })
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), token);
            }
        }
    }
}
