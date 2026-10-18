using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class PatientDocumentProtectionUpgrade
{
    public static async Task RunAsync(ApplicationDbContext db, PatientDocumentStorage storage,
        IWebHostEnvironment environment, CancellationToken token = default)
    {
        await db.Database.OpenConnectionAsync(token);
        var locked = false;
        try
        {
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'DRMC.PatientDocuments.ProtectionUpgrade', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=60000; SELECT @result;";
                command.CommandTimeout = 65;
                if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) < 0)
                    throw new InvalidOperationException("Could not acquire the document upgrade lock.");
                locked = true;
            }

            // Check every side, even on version 2 rows: older downloads could mark a two-sided
            // document upgraded after encrypting only its front image.
            var ids = await db.PatientIdDocuments.AsNoTracking().OrderBy(d => d.Id).Select(d => d.Id).ToListAsync(token);
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
                // One SaveChanges transaction publishes both sides together. Original paths
                // already contain ciphertext, including when this commit fails or is uncertain.
                await db.SaveChangesAsync(token);
                foreach (var name in previous)
                    if (!await db.PatientIdDocuments.AnyAsync(d => d.PatientUserId == document.PatientUserId &&
                        (d.FrontPhotoFileName == name || d.BackPhotoFileName == name), token))
                        await storage.DeleteLegacyAsync(document.PatientUserId, name, token);
                db.Entry(document).State = EntityState.Detached;
            }

            // Orphans can survive an uncertain commit or older failed registration. Protect
            // recognized images rather than deleting patient material without a retention rule.
            foreach (var directory in storage.Root.Entries())
            {
                if (!Directory.Exists(directory)) throw new InvalidDataException("Unexpected document storage entry.");
                var owner = Path.GetFileName(directory);
                foreach (var file in storage.Root.Entries(owner))
                {
                    if (Directory.Exists(file)) throw new InvalidDataException("Unexpected document storage directory.");
                    var name = Path.GetFileName(file);
                    await storage.ProtectLegacySourceAsync(storage.Root, [owner, name], token);
                    if (!PrivateStorageRoot.IsGuid(name))
                        File.Move(storage.Root.Resolve(owner, name), storage.Root.Resolve(owner, Guid.NewGuid().ToString("N")));
                }
            }
            foreach (var file in storage.TemporaryRoot.Entries())
            {
                if (Directory.Exists(file)) throw new InvalidDataException("Unexpected staged document directory.");
                // Preserve old GUID.idp names because existing protected staging tokens name them.
                await storage.ProtectLegacySourceAsync(storage.TemporaryRoot, [Path.GetFileName(file)], token);
            }
            RemoveObsoleteScratch(environment, token);
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
