using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class PatientDocumentStorageOptions
{
    public string RootPath { get; set; } = "App_Data/PatientIdDocuments";
    public string TemporaryPath { get; set; } = "App_Data/TempUploads";
    public int MaximumFileSizeMb { get; set; } = 5;
    public int TemporaryFileLifetimeMinutes { get; set; } = 30;
}

public sealed record StagedPatientDocument(string Token, string ContentType);
public sealed record StoredPatientDocument(string FileName, string ContentType, int StorageVersion);

public interface IPatientDocumentStorage
{
    Task<StagedPatientDocument> StageAsync(IFormFile file, string sessionId, CancellationToken cancellationToken = default);
    Task<StoredPatientDocument> CommitAsync(string token, string sessionId, string patientUserId, string side, CancellationToken cancellationToken = default);
    Task<StoredPatientDocument> StoreAsync(IFormFile file, string patientUserId, string side, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAsync(string patientUserId, string fileName, int storageVersion, CancellationToken cancellationToken = default);
    Task<StoredPatientDocument> MigrateLegacyAsync(string patientUserId, string fileName, string side, string contentType, CancellationToken cancellationToken = default);
    Task DeleteLegacyAsync(string patientUserId, string fileName, CancellationToken cancellationToken = default);
    Task DiscardStagedAsync(string? token, string sessionId, CancellationToken cancellationToken = default);
    Task DeleteStoredAsync(string patientUserId, string? fileName, CancellationToken cancellationToken = default);
    Task DeleteExpiredTemporaryFilesAsync(CancellationToken cancellationToken = default);
}

public sealed class PatientDocumentStorage : IPatientDocumentStorage
{
    private const int CurrentStorageVersion = 2;
    private readonly OcrRuntime _ocr;
    internal const int HistoricalMaximumBytes = 10 * 1024 * 1024;
    internal readonly PrivateStorageRoot Root;
    internal readonly PrivateStorageRoot TemporaryRoot;
    private readonly PatientDocumentStorageOptions _options;
    private readonly IDataProtector _fileProtector;
    private readonly IDataProtector _tokenProtector;
    internal string CreateProtectionCanary() => _tokenProtector.Protect("DRMC.PatientDocuments.Ready.v3");
    internal void VerifyProtectionCanary(string value)
    {
        if (_tokenProtector.Unprotect(value) != "DRMC.PatientDocuments.Ready.v3")
            throw new CryptographicException("Patient document protection verification failed.");
    }

    public PatientDocumentStorage(IWebHostEnvironment environment, IOptions<PatientDocumentStorageOptions> options, IDataProtectionProvider protectionProvider, OcrRuntime ocr)
    {
        _ocr = ocr;
        _options = options.Value;
        Root = new(environment, _options.RootPath);
        TemporaryRoot = new(environment, _options.TemporaryPath);
        if (Root.Overlaps(TemporaryRoot)) throw new InvalidOperationException("Permanent and temporary storage must be separate.");
        _fileProtector = protectionProvider.CreateProtector(KeyRingProtection.PatientDocumentPurpose);
        _tokenProtector = protectionProvider.CreateProtector(KeyRingProtection.PatientStagingTokenPurpose);
    }

    public async Task<StagedPatientDocument> StageAsync(IFormFile file, string sessionId, CancellationToken cancellationToken = default)
    {
        var (bytes, contentType) = await ValidateAndReadAsync(file, cancellationToken);
        var fileName = Guid.NewGuid().ToString("N");
        var directory = TemporaryRoot.Resolve();
        Directory.CreateDirectory(directory);
        var path = TemporaryRoot.Resolve(fileName);
        var payload = new StagingTokenPayload(sessionId, fileName, contentType, DateTime.UtcNow.AddMinutes(_options.TemporaryFileLifetimeMinutes));
        var protectedToken = _tokenProtector.Protect(JsonSerializer.Serialize(payload));
        await PrivateStorageIO.WriteNewAsync(path, _fileProtector.Protect(bytes), cancellationToken);
        return new StagedPatientDocument(protectedToken, contentType);
    }

    public async Task<StoredPatientDocument> CommitAsync(string token, string sessionId, string patientUserId, string side, CancellationToken cancellationToken = default)
    {
        StagingTokenPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<StagingTokenPayload>(_tokenProtector.Unprotect(token))
                ?? throw new InvalidDataException("Invalid upload token.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("Invalid upload token.", ex);
        }

        if (!string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal) || payload.ExpiresAtUtc <= DateTime.UtcNow || !IsSafeFileName(payload.FileName))
            throw new InvalidDataException("The staged upload is invalid or expired.");

        var source = TemporaryRoot.Resolve(payload.FileName);
        if (!File.Exists(source)) throw new InvalidDataException("The staged upload no longer exists.");
        _ = _fileProtector.Unprotect(await PrivateStorageIO.ReadAsync(source, HistoricalMaximumBytes + 4096, cancellationToken));
        var targetName = Guid.NewGuid().ToString("N");
        var targetDirectory = Root.Resolve(patientUserId);
        Directory.CreateDirectory(targetDirectory);
        File.Move(TemporaryRoot.Resolve(payload.FileName), Root.Resolve(patientUserId, targetName));
        return await Task.FromResult(new StoredPatientDocument(targetName, payload.ContentType, CurrentStorageVersion));
    }

    public async Task<StoredPatientDocument> StoreAsync(IFormFile file, string patientUserId, string side, CancellationToken cancellationToken = default)
    {
        var (bytes, contentType) = await ValidateAndReadAsync(file, cancellationToken);
        var targetName = Guid.NewGuid().ToString("N");
        var targetDirectory = Root.Resolve(patientUserId);
        Directory.CreateDirectory(targetDirectory);
        var path = Root.Resolve(patientUserId, targetName);
        await PrivateStorageIO.WriteNewAsync(path, _fileProtector.Protect(bytes), cancellationToken);
        return new StoredPatientDocument(targetName, contentType, CurrentStorageVersion);
    }

    public async Task<byte[]> ReadAsync(string patientUserId, string fileName, int storageVersion, CancellationToken cancellationToken = default)
    {
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var path = Root.Resolve(patientUserId, fileName);
        var bytes = await PrivateStorageIO.ReadAsync(path, HistoricalMaximumBytes + 4096, cancellationToken);
        return _fileProtector.Unprotect(bytes);
    }

    public async Task<StoredPatientDocument> MigrateLegacyAsync(string patientUserId, string fileName, string side, string contentType, CancellationToken cancellationToken = default)
    {
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var bytes = await ProtectLegacySourceAsync(Root, [patientUserId, fileName], cancellationToken);
        var actualType = ImageContentType(_fileProtector.Unprotect(bytes));
        if (PrivateStorageRoot.IsGuid(fileName)) return new(fileName, actualType, CurrentStorageVersion);
        var targetName = Guid.NewGuid().ToString("N");
        await PrivateStorageIO.WriteNewAsync(Root.Resolve(patientUserId, targetName), bytes, cancellationToken);
        return new StoredPatientDocument(targetName, actualType, CurrentStorageVersion);
    }

    public Task DeleteLegacyAsync(string patientUserId, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var source = Root.Resolve(patientUserId, fileName);
        if (File.Exists(source)) File.Delete(source);
        return Task.CompletedTask;
    }

    public Task DiscardStagedAsync(string? token, string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token)) return Task.CompletedTask;
        try
        {
            var payload = JsonSerializer.Deserialize<StagingTokenPayload>(_tokenProtector.Unprotect(token));
            if (payload is not null && string.Equals(payload.SessionId, sessionId, StringComparison.Ordinal) && IsSafeFileName(payload.FileName))
            {
                var path = TemporaryRoot.Resolve(payload.FileName);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // An invalid token has no trusted file target to delete.
        }
        return Task.CompletedTask;
    }

    public Task DeleteStoredAsync(string patientUserId, string? fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(fileName) || !IsSafeFileName(fileName)) return Task.CompletedTask;
        var path = Root.Resolve(patientUserId, fileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteExpiredTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var directory = TemporaryRoot.Resolve();
        if (!Directory.Exists(directory)) return Task.CompletedTask;
        var cutoff = DateTime.UtcNow.AddMinutes(-_options.TemporaryFileLifetimeMinutes);
        foreach (var file in TemporaryRoot.Entries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (!IsSafeFileName(name) || Directory.Exists(file)) throw new InvalidDataException("Unexpected staged document reference.");
            if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(TemporaryRoot.Resolve(name));
        }
        return Task.CompletedTask;
    }

    private async Task<(byte[] Bytes, string ContentType)> ValidateAndReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var maxBytes = (int)Math.Min(IdImageValidation.MaximumBytes, _options.MaximumFileSizeMb * 1024L * 1024L);
        if (file.Length <= 0 || file.Length > maxBytes) throw new InvalidDataException("Each ID photo must be between 1 byte and 5 MB.");
        await using var input = file.OpenReadStream();
        var result = await _ocr.ProcessAsync(input, recognize: false, cancellationToken: cancellationToken, maximumBytes: maxBytes);
        return (result.ValidatedBytes!, result.ContentType);
    }

    // Offline maintenance only. Replace plaintext at its original path before changing references;
    // a crash or uncertain database commit can therefore leave only encrypted duplicates.
    internal async Task<byte[]> ProtectLegacySourceAsync(PrivateStorageRoot root, string[] components, CancellationToken token)
    {
        var name = components[^1];
        if (!IsSafeFileName(name)) throw new InvalidDataException("Unexpected document reference.");
        var bytes = await PrivateStorageIO.ReadAsync(root.Resolve(components), HistoricalMaximumBytes + 4096, token);
        try
        {
            _ = ImageContentType(_fileProtector.Unprotect(bytes));
            return bytes;
        }
        catch (CryptographicException) when (!PrivateStorageRoot.IsGuid(name) && !name.EndsWith(".idp", StringComparison.OrdinalIgnoreCase) &&
            !bytes.AsSpan().StartsWith(new byte[] { 0x09, 0xf0, 0xc9, 0xf0 }))
        {
            _ = ImageContentType(bytes); // Recognize historical bytes; do not apply new-upload OCR restrictions.
            if (bytes.Length > HistoricalMaximumBytes) throw new InvalidDataException("Legacy document exceeds its size limit.");
            var encrypted = _fileProtector.Protect(bytes);
            if (!_fileProtector.Unprotect(encrypted).AsSpan().SequenceEqual(bytes)) throw new CryptographicException();
            var temporaryComponents = components[..^1].Append(Guid.NewGuid().ToString("N")).ToArray();
            var temporary = root.Resolve(temporaryComponents);
            await PrivateStorageIO.WriteNewAsync(temporary, encrypted, token);
            try { File.Move(root.Resolve(temporaryComponents), root.Resolve(components), overwrite: true); }
            finally { File.Delete(root.Resolve(temporaryComponents)); }
            return encrypted;
        }
    }

    public static string ImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new InvalidDataException("The stored ID image is invalid.");
    }

    internal static bool IsSafeFileName(string? fileName)
    {
        if (!PrivateStorageRoot.IsLeaf(fileName)) return false;
        if (PrivateStorageRoot.IsGuid(fileName)) return true;
        var stem = Path.GetFileNameWithoutExtension(fileName)!;
        if (stem.StartsWith("front_", StringComparison.Ordinal) || stem.StartsWith("back_", StringComparison.Ordinal)) stem = stem[(stem.IndexOf('_') + 1)..];
        return PrivateStorageRoot.IsGuid(stem) && Path.GetExtension(fileName!).ToLowerInvariant() is ".idp" or ".jpg" or ".jpeg" or ".png" or ".webp";
    }
    private sealed record StagingTokenPayload(string SessionId, string FileName, string ContentType, DateTime ExpiresAtUtc);
}

public sealed class TemporaryDocumentCleanupService(IServiceProvider services, ILogger<TemporaryDocumentCleanupService> logger, PortalWorkerHealth health) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPatientDocumentStorage>().DeleteExpiredTemporaryFilesAsync(stoppingToken);
                health.Success("temporary-documents");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Temporary ID document cleanup failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
