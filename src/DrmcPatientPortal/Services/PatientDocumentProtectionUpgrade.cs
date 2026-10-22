using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class PatientDocumentProtectionUpgrade
{
    public static async Task VerifyReadyAsync(ApplicationDbContext db, PatientDocumentStorage storage, CancellationToken token = default)
    {
        var state = await db.DocumentProtectionStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, token);
        if (state is not { Phase: 3, ProtectedCanary: not null })
            throw new InvalidOperationException("Patient document protection is incomplete. Stop all portal processes and run redstar-protection upgrade or patient-documents upgrade.");
        storage.VerifyProtectionCanary(state.ProtectedCanary);
    }

    public static async Task RunAsync(ApplicationDbContext db, PatientDocumentStorage storage,
        IWebHostEnvironment environment, CancellationToken token = default, bool verifyAll = false)
    {
        await db.Database.OpenConnectionAsync(token);
        var locked = false;
        try
        {
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'DRMC.PatientDocuments.ProtectionUpgrade', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
                command.CommandTimeout = 65;
                if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) < 0)
                    throw new InvalidOperationException("Could not acquire the document upgrade lock.");
                locked = true;
            }

            var state = await db.DocumentProtectionStates.SingleOrDefaultAsync(x => x.Id == 1, token);
            if (state is null)
            {
                state = new DocumentProtectionState { Id = 1 };
                db.DocumentProtectionStates.Add(state);
                await db.SaveChangesAsync(token);
            }
            else if (verifyAll)
            {
                state.Phase = 0; state.LastDocumentId = 0; state.LastOwner = ""; state.LastFile = ""; state.ProtectedCanary = null;
                await db.SaveChangesAsync(token);
            }
            if (state.Phase == 3) { await VerifyReadyAsync(db, storage, token); return; }

            // Offline only. Save durable progress after each record; interrupted commands resume.
            // Check every side, even on version 2 rows: older downloads could mark a two-sided
            // document upgraded after encrypting only its front image.
            while (state.Phase == 0)
            {
                var ids = await db.PatientIdDocuments.AsNoTracking().Where(d => d.Id > state.LastDocumentId)
                    .OrderBy(d => d.Id).Select(d => d.Id).Take(100).ToListAsync(token);
                if (ids.Count == 0) { state.Phase = 1; await db.SaveChangesAsync(token); break; }
                foreach (var id in ids)
                {
                    var document = await db.PatientIdDocuments.SingleAsync(d => d.Id == id, token);
                    if (document.StorageVersion is < 0 or > 2) throw new InvalidDataException("Unsupported document storage version.");
                    var previous = new[] { document.FrontPhotoFileName, document.BackPhotoFileName }.OfType<string>().Distinct().ToArray();
                    if (document.FrontPhotoFileName is { } front)
                    {
                        var stored = await storage.MigrateLegacyAsync(document.PatientUserId, front, "front", document.FrontPhotoContentType ?? "", token);
                        document.FrontPhotoFileName = stored.FileName;
                        document.FrontPhotoContentType = stored.ContentType;
                    }
                    if (document.BackPhotoFileName is { } back)
                    {
                        var stored = await storage.MigrateLegacyAsync(document.PatientUserId, back, "back", document.BackPhotoContentType ?? "", token);
                        document.BackPhotoFileName = stored.FileName;
                        document.BackPhotoContentType = stored.ContentType;
                    }
                    document.StorageVersion = 2;
                    state.LastDocumentId = document.Id;
                    // One SaveChanges transaction publishes both sides together. Original paths
                    // already contain ciphertext, including when this commit fails or is uncertain.
                    await db.SaveChangesAsync(token);
                    foreach (var name in previous)
                        if (!await db.PatientIdDocuments.AnyAsync(d => d.PatientUserId == document.PatientUserId &&
                            (d.FrontPhotoFileName == name || d.BackPhotoFileName == name), token))
                            await storage.DeleteLegacyAsync(document.PatientUserId, name, token);
                    db.Entry(document).State = EntityState.Detached;
                }
            }

            // Orphans can survive an uncertain commit or older failed registration. Protect
            // recognized images rather than deleting patient material without a retention rule.
            if (state.Phase == 1)
            {
                foreach (var directory in storage.Root.Entries().Order(StringComparer.Ordinal))
                {
                    if (!Directory.Exists(directory)) throw new InvalidDataException("Unexpected document storage entry.");
                    var owner = Path.GetFileName(directory);
                    if (StringComparer.Ordinal.Compare(owner, state.LastOwner) < 0) continue;
                    foreach (var file in storage.Root.Entries(owner).Order(StringComparer.Ordinal))
                    {
                        if (Directory.Exists(file)) throw new InvalidDataException("Unexpected document storage directory.");
                        var name = Path.GetFileName(file);
                        if (owner == state.LastOwner && StringComparer.Ordinal.Compare(name, state.LastFile) <= 0) continue;
                        await storage.ProtectLegacySourceAsync(storage.Root, [owner, name], token);
                        state.LastOwner = owner; state.LastFile = name;
                        await db.SaveChangesAsync(token);
                    }
                }
                state.Phase = 2; state.LastFile = "";
                await db.SaveChangesAsync(token);
            }
            foreach (var file in storage.TemporaryRoot.Entries().Order(StringComparer.Ordinal))
            {
                if (Directory.Exists(file)) throw new InvalidDataException("Unexpected staged document directory.");
                var name = Path.GetFileName(file);
                if (StringComparer.Ordinal.Compare(name, state.LastFile) <= 0) continue;
                // Preserve old GUID.idp names because existing protected staging tokens name them.
                await storage.ProtectLegacySourceAsync(storage.TemporaryRoot, [name], token);
                state.LastFile = name; await db.SaveChangesAsync(token);
            }
            RemoveObsoleteScratch(environment, token);
            state.Phase = 3; state.ProtectedCanary = storage.CreateProtectionCanary();
            await db.SaveChangesAsync(token);
            await VerifyReadyAsync(db, storage, token);
        }
        finally
        {
            try
            {
                if (locked)
                {
                    await using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'DRMC.PatientDocuments.ProtectionUpgrade', @LockOwner='Session';";
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private static void RemoveObsoleteScratch(IWebHostEnvironment environment, CancellationToken token)
    {
        var scratch = new PrivateStorageRoot(environment, "App_Data/OcrScratch");
        foreach (var directory in scratch.Entries())
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!PrivateStorageRoot.IsGuid(name) || !Directory.Exists(directory))
                throw new InvalidDataException("Unexpected obsolete OCR scratch entry.");
            foreach (var file in scratch.Entries(name))
            {
                var leaf = Path.GetFileName(file);
                if (leaf is not ("front" or "back") || Directory.Exists(file))
                    throw new InvalidDataException("Unexpected obsolete OCR scratch file.");
                File.Delete(scratch.Resolve(name, leaf));
            }
            Directory.Delete(scratch.Resolve(name));
        }
    }
}
