using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

/// <summary>Stages and scans ID photos using the same contract in signup and the authenticated wallet.</summary>
public sealed class IdDocumentScanService(
    IPatientDocumentStorage storage,
    IIdDocumentExtractionService extraction,
    IDataProtectionProvider protection,
    IOptions<PatientDocumentStorageOptions> options,
    ILogger<IdDocumentScanService> logger)
{
    private readonly IDataProtector receipts = protection.CreateProtector("DRMC.PatientDocuments.WalletScan.v1");

    public static string WalletSessionId(HttpContext context, string owner)
    {
        const string key = "IdWalletUploadSessionId";
        var nonce = context.Session.GetString(key);
        if (string.IsNullOrEmpty(nonce))
        {
            nonce = Guid.NewGuid().ToString("N");
            context.Session.SetString(key, nonce);
        }
        // Changing signed-in accounts in the same browser cannot transfer an upload.
        return $"wallet:{owner}:{nonce}";
    }

    public async Task<IActionResult> ExtractAsync(HttpContext context, string? idType, IFormFile? frontPhoto,
        IFormFile? backPhoto, string sessionId, string? ownerId = null)
    {
        var selected = PhilippineIdTypes.GetByName(idType);
        // Signup historically accepts nonempty type aliases for its scan-only handler.
        // Wallet writes use the registered canonical types exclusively.
        if (string.IsNullOrWhiteSpace(idType) || (ownerId is not null && selected is null) || frontPhoto is not { Length: > 0 })
            return new JsonResult(new { success = false, message = "Please select an ID type and provide a clear front photo." }) { StatusCode = 400 };
        idType = selected?.Name ?? idType;
        StagedPatientDocument? front = null, back = null;
        var retain = false;
        try
        {
            front = await storage.StageAsync(frontPhoto, sessionId, context.RequestAborted);
            if (backPhoto is { Length: > 0 }) back = await storage.StageAsync(backPhoto, sessionId, context.RequestAborted);
            await using var frontStream = frontPhoto.OpenReadStream();
            using var backStream = backPhoto is { Length: > 0 } ? backPhoto.OpenReadStream() : null;
            var result = await extraction.ExtractFromStreamsAsync(idType, frontStream, backStream, context.RequestAborted);
            var confidence = Score(result.MeanConfidence);
            var receipt = ownerId is null ? null : CreateReceipt(ownerId, sessionId, idType,
                front.Token, back?.Token, confidence, result.Success);
            retain = true;
            return new JsonResult(new IdScanResponse
            {
                Success = result.Success, MeanConfidence = confidence,
                TempFrontToken = front.Token, TempBackToken = back?.Token, ScanReceipt = receipt,
                FirstName = result.FirstName.Found ? result.FirstName.Value ?? "" : "",
                MiddleName = result.MiddleName.Found ? result.MiddleName.Value ?? "" : "",
                LastName = result.LastName.Found ? result.LastName.Value ?? "" : "",
                FullName = result.FullName.Found ? result.FullName.Value ?? "" : "",
                IdNumber = result.IdNumber.Found ? result.IdNumber.Value ?? "" : "",
                DateOfBirth = result.DateOfBirth.Found ? result.DateOfBirth.Value.ToString("yyyy-MM-dd") : "",
                Address = result.Address.Found ? result.Address.Value ?? "" : "",
                Sex = result.Sex.Found ? result.Sex.Value ?? "" : "",
                BloodType = result.BloodType.Found ? result.BloodType.Value ?? "" : "",
                FoundSummary = result.FoundFieldsSummary
            });
        }
        catch (OcrBusyException error)
        {
            context.Response.Headers.RetryAfter = "5";
            return Failure(error.Message, 429);
        }
        catch (OcrTimeoutException error) { return Failure(error.Message, 503); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return new StatusCodeResult(499); }
        catch (OcrDependencyUnavailableException error)
        {
            logger.LogError(error, "ID scanning dependencies unavailable.");
            return Failure("ID scanning is temporarily unavailable. Please try again later.", 503);
        }
        catch (InvalidDataException error) { return Failure(error.Message, 400); }
        catch (Exception error)
        {
            logger.LogError(error, "ID photo extraction failed.");
            return Failure("Could not read ID photo. You can proceed with manual entry.", 200);
        }
        finally
        {
            if (!retain)
                foreach (var token in new[] { front?.Token, back?.Token })
                    try { await storage.DiscardStagedAsync(token, sessionId, CancellationToken.None); }
                    catch (Exception) { logger.LogWarning("Could not discard a staged ID photo."); }
        }
    }

    internal string CreateReceipt(string owner, string session, string idType, string front, string? back,
        float confidence, bool success) => receipts.Protect(JsonSerializer.Serialize(new ScanReceipt(
            owner, session, idType, front, back ?? "", Score(confidence), success,
            DateTime.UtcNow.AddMinutes(options.Value.TemporaryFileLifetimeMinutes))));

    public async Task<WalletPhotoCommit> CommitWalletPhotosAsync(IdWalletInput input, string owner, string session,
        CancellationToken token = default)
    {
        ScanReceipt? receipt = null;
        var hasStaged = !string.IsNullOrWhiteSpace(input.TempFrontPhotoToken) ||
            !string.IsNullOrWhiteSpace(input.TempBackPhotoToken) || !string.IsNullOrWhiteSpace(input.ScanReceipt);
        if (hasStaged)
        {
            try
            {
                receipt = JsonSerializer.Deserialize<ScanReceipt>(receipts.Unprotect(input.ScanReceipt ?? ""));
                if (receipt is null || receipt.Owner != owner || receipt.Session != session ||
                    receipt.IdType != PhilippineIdTypes.GetByName(input.IdType)?.Name || receipt.ExpiresAtUtc <= DateTime.UtcNow ||
                    receipt.Front != input.TempFrontPhotoToken || string.IsNullOrWhiteSpace(receipt.Front) ||
                    receipt.Back != (input.TempBackPhotoToken ?? "") || !float.IsFinite(receipt.Confidence))
                    throw new InvalidDataException();
            }
            catch (Exception error) when (error is CryptographicException or JsonException or InvalidDataException or ArgumentException or FormatException)
            { throw new InvalidDataException("The scanned photos are invalid or expired. Scan again or select the photos again.", error); }
        }
        else if (input.FrontPhoto is not { Length: > 0 })
            throw new InvalidDataException("Capture or upload the front of your ID.");

        StoredPatientDocument? front = null, back = null;
        try
        {
            front = receipt is not null
                ? await storage.CommitAsync(receipt.Front, session, owner, "front", token)
                : await storage.StoreAsync(input.FrontPhoto!, owner, "front", token);
            if (receipt is not null && receipt.Back.Length > 0)
                back = await storage.CommitAsync(receipt.Back, session, owner, "back", token);
            else if (receipt is null && input.BackPhoto is { Length: > 0 })
                back = await storage.StoreAsync(input.BackPhoto, owner, "back", token);
            return new(front, back, receipt?.Confidence ?? 0, receipt?.Success != true);
        }
        catch
        {
            await DeletePhotosAsync(owner, front, back);
            throw;
        }
    }

    public async Task DeletePhotosAsync(string owner, StoredPatientDocument? front, StoredPatientDocument? back)
    {
        foreach (var file in new[] { front?.FileName, back?.FileName }.OfType<string>().Distinct())
            try { await storage.DeleteStoredAsync(owner, file, CancellationToken.None); }
            catch (Exception error) { logger.LogWarning("ID photo cleanup unavailable ({ErrorType}).", error.GetType().Name); }
    }

    private static float Score(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static JsonResult Failure(string message, int status) => new(new { success = false, message }) { StatusCode = status };
    private sealed record ScanReceipt(string Owner, string Session, string IdType, string Front, string Back,
        float Confidence, bool Success, DateTime ExpiresAtUtc);
}

public sealed record WalletPhotoCommit(StoredPatientDocument Front, StoredPatientDocument? Back, float Confidence, bool IsManualEntry);

public sealed class IdScanResponse
{
    public bool Success { get; init; }
    public float MeanConfidence { get; init; }
    public string? TempFrontToken { get; init; }
    public string? TempBackToken { get; init; }
    public string? ScanReceipt { get; init; }
    public string FirstName { get; init; } = "";
    public string MiddleName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string FullName { get; init; } = "";
    public string IdNumber { get; init; } = "";
    public string DateOfBirth { get; init; } = "";
    public string Address { get; init; } = "";
    public string Sex { get; init; } = "";
    public string BloodType { get; init; } = "";
    public Dictionary<string, string> FoundSummary { get; init; } = [];
}

public sealed class IdWalletInput : IValidatableObject
{
    [Required, StringLength(120)] public string IdType { get; set; } = "";
    [StringLength(120)] public string? OtherGovernmentIdName { get; set; }
    [StringLength(60)] public string? FirstName { get; set; }
    [StringLength(60)] public string? MiddleName { get; set; }
    [StringLength(60)] public string? LastName { get; set; }
    [StringLength(10)] public string? Suffix { get; set; }
    [DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
    [StringLength(40)] public string? IdNumber { get; set; }
    [StringLength(200)] public string? Address { get; set; }
    [StringLength(20)] public string? Sex { get; set; }
    [StringLength(10)] public string? BloodType { get; set; }
    public IFormFile? FrontPhoto { get; set; }
    public IFormFile? BackPhoto { get; set; }
    [StringLength(4096)] public string? TempFrontPhotoToken { get; set; }
    [StringLength(4096)] public string? TempBackPhotoToken { get; set; }
    [StringLength(16384)] public string? ScanReceipt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PhilippineIdTypes.GetByName(IdType) is null)
            yield return new("Select a government-issued ID.", [nameof(IdType)]);
        if (FrontPhoto is not { Length: > 0 } && string.IsNullOrWhiteSpace(TempFrontPhotoToken))
            yield return new("Capture or upload the front of your ID.", [nameof(FrontPhoto)]);
        if (DateOfBirth?.Date > ClinicalClock.Today)
            yield return new("Date of birth cannot be in the future.", [nameof(DateOfBirth)]);
    }
}
