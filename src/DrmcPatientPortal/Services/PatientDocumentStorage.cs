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
    private readonly IWebHostEnvironment _environment;
    private readonly PatientDocumentStorageOptions _options;
    private readonly IDataProtector _fileProtector;
    private readonly IDataProtector _tokenProtector;

    public PatientDocumentStorage(IWebHostEnvironment environment, IOptions<PatientDocumentStorageOptions> options, IDataProtectionProvider protectionProvider, OcrRuntime ocr)
    {
        _ocr = ocr;
        _environment = environment;
        _options = options.Value;
        _fileProtector = protectionProvider.CreateProtector("DRMC.PatientDocuments.File.v2");
        _tokenProtector = protectionProvider.CreateProtector("DRMC.PatientDocuments.StagingToken.v1");
    }

    public async Task<StagedPatientDocument> StageAsync(IFormFile file, string sessionId, CancellationToken cancellationToken = default)
    {
        var (bytes, contentType) = await ValidateAndReadAsync(file, cancellationToken);
        var fileName = $"{Guid.NewGuid():N}.idp";
        var directory = ResolveDirectory(_options.TemporaryPath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        try
        {
            await File.WriteAllBytesAsync(path, _fileProtector.Protect(bytes), cancellationToken);
            var payload = new StagingTokenPayload(sessionId, fileName, contentType, DateTime.UtcNow.AddMinutes(_options.TemporaryFileLifetimeMinutes));
            return new StagedPatientDocument(_tokenProtector.Protect(JsonSerializer.Serialize(payload)), contentType);
        }
        catch { File.Delete(path); throw; }
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

        var source = ResolveFile(_options.TemporaryPath, payload.FileName);
        if (!File.Exists(source)) throw new InvalidDataException("The staged upload no longer exists.");

        var targetName = $"{NormalizeSide(side)}_{Guid.NewGuid():N}.idp";
        var targetDirectory = ResolveDirectory(Path.Combine(_options.RootPath, patientUserId));
        Directory.CreateDirectory(targetDirectory);
        File.Move(source, Path.Combine(targetDirectory, targetName));
        return await Task.FromResult(new StoredPatientDocument(targetName, payload.ContentType, CurrentStorageVersion));
    }

    public async Task<StoredPatientDocument> StoreAsync(IFormFile file, string patientUserId, string side, CancellationToken cancellationToken = default)
    {
        var (bytes, contentType) = await ValidateAndReadAsync(file, cancellationToken);
        var targetName = $"{NormalizeSide(side)}_{Guid.NewGuid():N}.idp";
        var targetDirectory = ResolveDirectory(Path.Combine(_options.RootPath, patientUserId));
        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, targetName);
        try { await File.WriteAllBytesAsync(path, _fileProtector.Protect(bytes), cancellationToken); }
        catch { File.Delete(path); throw; }
        return new StoredPatientDocument(targetName, contentType, CurrentStorageVersion);
    }

    public async Task<byte[]> ReadAsync(string patientUserId, string fileName, int storageVersion, CancellationToken cancellationToken = default)
    {
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var path = ResolveFile(Path.Combine(_options.RootPath, patientUserId), fileName);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return storageVersion >= CurrentStorageVersion || fileName.EndsWith(".idp", StringComparison.OrdinalIgnoreCase)
            ? _fileProtector.Unprotect(bytes)
            : bytes;
    }

    public async Task<StoredPatientDocument> MigrateLegacyAsync(string patientUserId, string fileName, string side, string contentType, CancellationToken cancellationToken = default)
    {
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var source = ResolveFile(Path.Combine(_options.RootPath, patientUserId), fileName);
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        // Preserve historical documents unchanged; new-upload checks do not apply on download/migration.
        var targetName = $"{NormalizeSide(side)}_{Guid.NewGuid():N}.idp";
        var target = ResolveFile(Path.Combine(_options.RootPath, patientUserId), targetName);
        await File.WriteAllBytesAsync(target, _fileProtector.Protect(bytes), cancellationToken);
        return new StoredPatientDocument(targetName, contentType, CurrentStorageVersion);
    }

    public Task DeleteLegacyAsync(string patientUserId, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafeFileName(fileName)) throw new FileNotFoundException();
        var source = ResolveFile(Path.Combine(_options.RootPath, patientUserId), fileName);
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
                var path = ResolveFile(_options.TemporaryPath, payload.FileName);
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
        var path = ResolveFile(Path.Combine(_options.RootPath, patientUserId), fileName);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteExpiredTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var directory = ResolveDirectory(_options.TemporaryPath);
        if (!Directory.Exists(directory)) return Task.CompletedTask;
        var cutoff = DateTime.UtcNow.AddMinutes(-_options.TemporaryFileLifetimeMinutes);
        foreach (var file in Directory.EnumerateFiles(directory, "*.idp", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
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

    private string ResolveDirectory(string configuredPath)
    {
        var root = Path.GetFullPath(_environment.ContentRootPath);
        var candidate = Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(root, configuredPath));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(configuredPath))
            throw new InvalidOperationException("Document storage path escapes the content root.");
        return candidate;
    }

    private string ResolveFile(string directory, string fileName)
    {
        var root = ResolveDirectory(directory);
        var candidate = Path.GetFullPath(Path.Combine(root, fileName));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException();
        return candidate;
    }

    private static bool IsSafeFileName(string fileName) => !string.IsNullOrWhiteSpace(fileName) && fileName == Path.GetFileName(fileName);
    private static string NormalizeSide(string side) => string.Equals(side, "back", StringComparison.OrdinalIgnoreCase) ? "back" : "front";
    private sealed record StagingTokenPayload(string SessionId, string FileName, string ContentType, DateTime ExpiresAtUtc);
}

public sealed class TemporaryDocumentCleanupService(IServiceProvider services, ILogger<TemporaryDocumentCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPatientDocumentStorage>().DeleteExpiredTemporaryFilesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Temporary ID document cleanup failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
