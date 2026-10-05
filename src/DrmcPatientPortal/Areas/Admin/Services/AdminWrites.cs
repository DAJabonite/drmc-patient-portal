using System.Data;
using System.Security.Claims;
using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed record WriteResult(bool Succeeded, string? Error = null, bool Conflict = false)
{
    public static WriteResult Success => new(true);
    public static WriteResult Invalid(string error) => new(false, error);
    public static WriteResult Stale => new(false, "This record changed. Reload it and review the latest version before trying again.", true);
}

public sealed class AdminWrites(IServiceScopeFactory scopes)
{
    public async Task<WriteResult> RunAsync(Func<ApplicationDbContext, Task<WriteResult>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var committing = false;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var result = await operation(db);
                if (!result.Succeeded) return result;
                committing = true;
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException) { return WriteResult.Stale; }
            catch (Exception error) when (!committing && SqlError(error) == 1205)
            {
                if (attempt == 2) return WriteResult.Invalid("The record is busy. Please try again shortly.");
                await Task.Delay(TimeSpan.FromMilliseconds(75 * (attempt + 1)), cancellationToken);
            }
            catch (Exception error) when (committing && error is not OperationCanceledException)
            {
                return WriteResult.Invalid("The save outcome could not be confirmed. Reload the records before trying again.");
            }
            catch (DbUpdateException)
            {
                return WriteResult.Invalid("The change could not be saved. Review duplicates, relationships and the current record, then try again.");
            }
            catch (SqlException)
            {
                return WriteResult.Invalid("The change could not be saved. Please reload the record and try again shortly.");
            }
        }
        throw new InvalidOperationException("Unreachable write retry state.");
    }

    private static int? SqlError(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SqlException sql) return sql.Number;
        return null;
    }
}

public static class AdminAudit
{
    public static Dictionary<string, object?> Values(ApplicationDbContext db, object entity) =>
        db.Entry(entity).Properties.Where(p => p.Metadata.Name is not "RowVersion" and not "ViewCount" and not "Id")
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    public static async Task AddAsync(ApplicationDbContext db, ClaimsPrincipal actor, string action, string entity,
        string key, int? patientId = null, Dictionary<string, object?>? before = null, Dictionary<string, object?>? after = null,
        IEnumerable<string>? fields = null, CancellationToken cancellationToken = default)
    {
        var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("An audit actor is required.");
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (account is null) throw new DbUpdateException("The audit actor is no longer available.");
        var email = account.Email;
        var changed = fields ?? (before ?? after ?? []).Keys.Where(k => before is null || after is null ||
            !Equals(before.GetValueOrDefault(k), after.GetValueOrDefault(k)));
        var publicContent = entity is "Doctors" or "PublicAdvisories";
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorId = actorId, ActorEmail = email ?? string.Empty, TimestampUtc = DateTime.UtcNow,
            Action = action, Entity = entity, RecordKey = key, SubjectPatientId = patientId,
            ChangedFields = JsonSerializer.Serialize(changed.OrderBy(x => x).ToArray()),
            OldValues = publicContent && before is not null ? JsonSerializer.Serialize(before) : null,
            NewValues = publicContent && after is not null ? JsonSerializer.Serialize(after) : null
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ViewAsync(ApplicationDbContext db, ClaimsPrincipal actor, string entity,
        IEnumerable<int> patients, string key, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var patient in patients.Distinct())
                await AddAsync(db, actor, "View", entity, key, patient, fields: [], cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new DrmcPatientPortal.Services.AuditLogPersistenceException("Required staff access logging failed.", error);
        }
    }

    public static byte[]? Version(string? text)
    {
        try { var value = Convert.FromBase64String(text ?? ""); return value.Length == 8 ? value : null; }
        catch (FormatException) { return null; }
    }
}
