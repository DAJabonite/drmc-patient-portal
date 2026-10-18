using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DrmcPatientPortal.Services;

public static class RedStarProtectionUpgrade
{
    private static DbCommand Command(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        return command;
    }
    private static void Parameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
    private sealed record Row(object Id, int Version, Dictionary<string, string?> Values);

    public static async Task VerifyReadyAsync(ApplicationDbContext db, CancellationToken token = default)
    {
        var state = await db.RedStarProtectionStates.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, token);
        if (state?.Ready != true) throw new InvalidOperationException("Private registry protection is incomplete. Stop all portal processes and run redstar-protection upgrade.");
        db.Phi.VerifyCanary(state.ProtectedCanary);
        if (state.BlindKeyCheck != db.Phi.KeyCheck) throw new CryptographicException("Private registry lookup key verification failed.");
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await db.Database.OpenConnectionAsync(token);
        try
        {
            foreach (var table in RedStarPhiProtection.Tables)
            {
                await using var query = Command(connection, $"SELECT COUNT_BIG(*) FROM [dbo].[{table.Table}] WHERE [PhiProtectionVersion] <> 1");
                if ((long)(await query.ExecuteScalarAsync(token))! != 0)
                    throw new InvalidOperationException("Private registry protection contains legacy rows. Run the offline upgrade.");
            }
        }
        finally { if (!wasOpen) await db.Database.CloseConnectionAsync(); }
    }

    public static async Task RunAsync(ApplicationDbContext db, CancellationToken token = default)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(token)) await creator.CreateAsync(token);
        await db.Database.OpenConnectionAsync(token);
        var connection = db.Database.GetDbConnection();
        var locked = false;
        try
        {
            await using (var acquire = Command(connection, "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'DRMC.RedStar.ProtectionUpgrade', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;"))
            {
                if (Convert.ToInt32(await acquire.ExecuteScalarAsync(token)) < 0) throw new InvalidOperationException("Another private registry upgrade is running.");
                locked = true;
            }
            await db.Database.MigrateAsync(token);
            var state = await db.RedStarProtectionStates.SingleOrDefaultAsync(s => s.Id == 1, token);
            if (state is null)
            {
                foreach (var table in RedStarPhiProtection.Tables)
                {
                    await using var query = Command(connection, $"SELECT COUNT_BIG(*) FROM [dbo].[{table.Table}] WHERE [PhiProtectionVersion] <> 0");
                    if ((long)(await query.ExecuteScalarAsync(token))! != 0)
                        throw new InvalidOperationException("Protected rows exist without their key binding. Restore the original protection state and keys.");
                }
                db.Phi.InitializeKey();
                state = new RedStarProtectionState { Id = 1, BlindKeyCheck = db.Phi.KeyCheck, ProtectedCanary = db.Phi.CreateCanary() };
                db.RedStarProtectionStates.Add(state);
            }
            else
            {
                db.Phi.VerifyCanary(state.ProtectedCanary);
                if (state.BlindKeyCheck != db.Phi.KeyCheck) throw new CryptographicException("Private registry lookup key verification failed.");
                state.Ready = false;
            }
            await db.SaveChangesAsync(token);
            // Check all codes before changing any patient rows. Do not merge or rename
            // colliding legacy identities; staff must reconcile them explicitly.
            var seen = new Dictionary<string, object>(StringComparer.Ordinal);
            await foreach (var row in ReadAll(connection, RedStarPhiProtection.Tables[0], token))
            {
                var code = Plain(db.Phi, RedStarPhiProtection.Tables[0], row, "PatientCode")!;
                var index = db.Phi.PatientCodeIndex(code);
                if (seen.TryGetValue(index, out var id)) throw new InvalidOperationException($"Private patient code conflict between record IDs {id} and {row.Id}. Reconcile before continuing.");
                seen[index] = row.Id;
            }
            foreach (var table in RedStarPhiProtection.Tables)
            {
                while (true)
                {
                    var rows = await ReadBatch(connection, table, token);
                    if (rows.Count == 0) break;
                    await using var transaction = await connection.BeginTransactionAsync(token);
                    foreach (var row in rows)
                    {
                        var values = new Dictionary<string, string?>();
                        foreach (var field in table.Fields)
                        {
                            var value = row.Values[field];
                            values[field] = value is null ? null : db.Phi.Protect(table.Entity.Name, field, value);
                            if (value is not null && db.Phi.Unprotect(table.Entity.Name, field, values[field]!) != value)
                                throw new CryptographicException("Private registry encryption round-trip failed.");
                        }
                        foreach (var pair in Indexes(db.Phi, table, row.Values)) values[pair.Key] = pair.Value;
                        var assignments = values.Keys.Select((name, i) => $"[{name}]=@p{i}");
                        await using var update = Command(connection, $"UPDATE [dbo].[{table.Table}] SET {string.Join(",", assignments)}, [PhiProtectionVersion]=1 WHERE [Id]=@id AND [PhiProtectionVersion]=0", transaction);
                        var position = 0;
                        foreach (var value in values.Values) Parameter(update, "@p" + position++, value);
                        Parameter(update, "@id", row.Id);
                        if (await update.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Private registry changed during maintenance. Stop all application processes before upgrading.");
                    }
                    await transaction.CommitAsync(token);
                }
                await foreach (var row in ReadAll(connection, table, token))
                {
                    if (row.Version != 1) throw new InvalidOperationException("Unsupported private registry protection version.");
                    var plaintext = table.Fields.ToDictionary(field => field, field => Plain(db.Phi, table, row, field));
                    foreach (var expected in Indexes(db.Phi, table, plaintext))
                        if (row.Values[expected.Key] != expected.Value) throw new CryptographicException("Private registry lookup verification failed.");
                }
            }
            state.Ready = true;
            await db.SaveChangesAsync(token);
        }
        finally
        {
            if (locked)
            {
                await using var release = Command(connection, "EXEC sys.sp_releaseapplock @Resource=N'DRMC.RedStar.ProtectionUpgrade', @LockOwner='Session';");
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    private static string? Plain(RedStarPhiProtection protection, RedStarProtectedTable table, Row row, string field) =>
        row.Values[field] is not { } value ? null : row.Version switch
        {
            0 => value, 1 => protection.Unprotect(table.Entity.Name, field, value),
            _ => throw new InvalidOperationException("Unsupported private registry protection version.")
        };
    private static Dictionary<string, string?> Indexes(RedStarPhiProtection protection, RedStarProtectedTable table, Dictionary<string, string?> values) =>
        table.Entity == typeof(RedStarPatient) ? new()
        {
            ["PatientCodeBlindIndex"] = protection.PatientCodeIndex(values["PatientCode"]!),
            ["HospitalNumberBlindIndex"] = protection.HospitalNumberIndex(values["HospitalNumber"]),
            ["PortalUserIdBlindIndex"] = protection.AccountIndex(values["PortalUserId"])
        } : table.Entity == typeof(RedStarReminder) ? new() { ["PortalUserIdBlindIndex"] = protection.AccountIndex(values["PortalUserId"]) } : [];
    private static string[] Columns(RedStarProtectedTable table) => ["Id", "PhiProtectionVersion", .. table.Fields,
        .. table.Entity == typeof(RedStarPatient) ? new[] { "PatientCodeBlindIndex", "HospitalNumberBlindIndex", "PortalUserIdBlindIndex" } :
           table.Entity == typeof(RedStarReminder) ? new[] { "PortalUserIdBlindIndex" } : Array.Empty<string>()];
    private static Row Read(DbDataReader reader, string[] columns) => new(reader.GetValue(0), reader.GetInt32(1),
        columns.Skip(2).Select((field, index) => (field, value: reader.IsDBNull(index + 2) ? null : reader.GetString(index + 2)))
            .ToDictionary(pair => pair.field, pair => pair.value));
    private static async Task<List<Row>> ReadBatch(DbConnection connection, RedStarProtectedTable table, CancellationToken token)
    {
        var columns = Columns(table);
        await using var query = Command(connection, $"SELECT TOP(100) {string.Join(",", columns.Select(c => $"[{c}]"))} FROM [dbo].[{table.Table}] WHERE [PhiProtectionVersion]=0 ORDER BY [Id]");
        await using var reader = await query.ExecuteReaderAsync(token);
        var rows = new List<Row>();
        while (await reader.ReadAsync(token)) rows.Add(Read(reader, columns));
        return rows;
    }
    private static async IAsyncEnumerable<Row> ReadAll(DbConnection connection, RedStarProtectedTable table,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var columns = Columns(table);
        await using var query = Command(connection, $"SELECT {string.Join(",", columns.Select(c => $"[{c}]"))} FROM [dbo].[{table.Table}] ORDER BY [Id]");
        await using var reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) yield return Read(reader, columns);
    }
}
