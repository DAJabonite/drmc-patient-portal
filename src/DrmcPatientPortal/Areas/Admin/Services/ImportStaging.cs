using System.Security.Cryptography;
using System.Text.Json;
using DrmcPatientPortal.Areas.Admin.Models;
using Microsoft.AspNetCore.DataProtection;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class ImportStaging
{
    private readonly string root;
    private readonly IDataProtector protector;
    public ImportStaging(IConfiguration configuration, IWebHostEnvironment environment, IDataProtectionProvider protection)
    {
        root = Path.GetFullPath(configuration["Imports:StagingPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMC", "ImportStaging"));
        var web = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (root.Equals(web.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || root.StartsWith(web, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Import staging must be outside the web root.");
        Directory.CreateDirectory(root);
        protector = protection.CreateProtector("DRMC.Admin.ImportStaging.v1");
    }
    private string PathFor(Guid id) => Path.Combine(root, id.ToString("N") + ".stage");
    public static string CommandHash(ImportCommand command) => FileHash(JsonSerializer.SerializeToUtf8Bytes(command));
    public async Task SaveCommandAsync(Guid id, ImportCommand command, CancellationToken token)
    {
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(command));
        if (bytes.Length > 4 * 1024 * 1024) throw new ImportRejectedException("The reconciliation command exceeds its storage limit.");
        var path = PathFor(id) + ".command";
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<ImportCommand> LoadCommandAsync(Guid id, CancellationToken token)
    {
        try
        {
            var path = PathFor(id) + ".command";
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new CryptographicException();
            return JsonSerializer.Deserialize<ImportCommand>(protector.Unprotect(await File.ReadAllBytesAsync(path, token))) ?? throw new CryptographicException();
        }
        catch (Exception error) when (error is CryptographicException or JsonException or IOException)
        { throw new ImportRejectedException("ValidationChanged"); }
    }
    public async Task SaveAsync(Guid id, ImportEnvelope envelope, CancellationToken token)
    {
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(envelope));
        if (bytes.Length > 100 * 1024 * 1024) throw new ImportRejectedException("The staged batch exceeds its storage limit.");
        var temporary = PathFor(id) + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, PathFor(id), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<ImportEnvelope> LoadAsync(Guid id, CancellationToken token)
    {
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path) || new FileInfo(path).Length > 100 * 1024 * 1024) throw new ImportRejectedException("Staged content is unavailable. Cancel this batch and upload it again.");
            return JsonSerializer.Deserialize<ImportEnvelope>(protector.Unprotect(await File.ReadAllBytesAsync(path, token))) ?? throw new CryptographicException();
        }
        catch (Exception error) when (error is CryptographicException or JsonException or IOException)
        { throw new ImportRejectedException("Staged content cannot be verified. Cancel this batch and upload it again."); }
    }
    public void Purge(Guid id)
    {
        File.Delete(PathFor(id));
        foreach (var temporary in Directory.EnumerateFiles(root, id.ToString("N") + ".stage.*")) File.Delete(temporary);
    }
    public IEnumerable<Guid> OldOrphans(DateTime before) => Directory.EnumerateFiles(root, "*.stage*")
        .Where(path => File.GetLastWriteTimeUtc(path) < before)
        .Select(path => Guid.TryParseExact(Path.GetFileName(path).Split('.')[0], "N", out var id) ? (Guid?)id : null)
        .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
    public static string FileHash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
    public static string ApprovalHash(ImportEnvelope envelope) => FileHash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        File = FileHash(envelope.Content), envelope.Template, envelope.Format, Version = ImportTemplates.ValidationVersion,
        Mappings = envelope.Mappings.OrderBy(mapping => mapping.SourcePatientKey, StringComparer.Ordinal).ToArray()
    }));
}
