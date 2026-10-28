using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class MyIdsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLogService;
    private readonly IdDocumentScanService _scans;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MyIdsModel> _logger;

    public MyIdsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLogService,
        IdDocumentScanService scans,
        IServiceScopeFactory scopes,
        ILogger<MyIdsModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _auditLogService = auditLogService;
        _scans = scans;
        _scopes = scopes;
        _logger = logger;
    }

    public IReadOnlyList<IdCardViewModel> Documents { get; private set; } = Array.Empty<IdCardViewModel>();
    [BindProperty] public IdWalletInput Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int idPage = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        await LoadDocumentsAsync(user, idPage);
        return Page();
    }

    public async Task<IActionResult> OnPostExtractIdAsync(string? idType, IFormFile? frontPhoto, IFormFile? backPhoto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        return await _scans.ExtractAsync(HttpContext, idType, frontPhoto, backPhoto,
            IdDocumentScanService.WalletSessionId(HttpContext, user.Id), user.Id);
    }

    public async Task<IActionResult> OnPostSaveIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!ModelState.IsValid)
            return await SaveFailureAsync(user, 400, resetScan: false);

        WalletPhotoCommit? photos = null;
        PatientIdDocument? document = null;
        var commitAttempted = false;
        var saved = false;
        var preserve = false;
        var failureStatus = 503;
        try
        {
            photos = await _scans.CommitWalletPhotosAsync(Input, user.Id,
                IdDocumentScanService.WalletSessionId(HttpContext, user.Id), HttpContext.RequestAborted);
            document = new PatientIdDocument
            {
                PatientUserId = user.Id,
                IdType = PhilippineIdTypes.GetByName(Input.IdType)!.Name,
                IdNumber = Input.IdNumber?.Trim() ?? "",
                FrontPhotoFileName = photos.Front.FileName,
                BackPhotoFileName = photos.Back?.FileName,
                FrontPhotoContentType = photos.Front.ContentType,
                BackPhotoContentType = photos.Back?.ContentType,
                StorageVersion = photos.Front.StorageVersion,
                OcrConfidence = photos.Confidence,
                IsManualEntry = photos.IsManualEntry,
                CapturedAt = DateTime.UtcNow,
                ExtractedFieldsJson = JsonSerializer.Serialize(new
                {
                    FirstName = Input.FirstName?.Trim(), MiddleName = Input.MiddleName?.Trim(),
                    LastName = Input.LastName?.Trim(), Suffix = Input.Suffix?.Trim(),
                    DateOfBirth = Input.DateOfBirth?.ToString("yyyy-MM-dd"),
                    IdNumber = Input.IdNumber?.Trim(), Address = Input.Address?.Trim(),
                    Sex = Input.Sex?.Trim(), BloodType = Input.BloodType?.Trim(),
                    OtherGovernmentIdName = Input.OtherGovernmentIdName?.Trim()
                })
            };
            await using (var transaction = await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted))
            {
                _db.PatientIdDocuments.Add(document);
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
                await _auditLogService.LogAsync(user.Id, "ADD_ID_DOCUMENT", $"PatientIdDocument/{document.Id}",
                    "Added a government ID photo and reviewed document details.",
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                commitAttempted = true;
                await transaction.CommitAsync(CancellationToken.None);
                saved = true;
            }
        }
        catch (Exception error)
        {
            failureStatus = error is InvalidDataException && !commitAttempted ? 400 : 503;
            _logger.LogWarning("Saving an ID document failed ({ErrorType}).", error.GetType().Name);
            if (commitAttempted && document is not null)
            {
                var committed = await VerifyCommitAsync(document);
                saved = committed == true;
                preserve = committed is null;
            }
            ErrorMessage = preserve
                ? "We could not confirm whether your ID was saved. Reload My IDs before trying again."
                : error is InvalidDataException && !commitAttempted ? error.Message
                : error is OcrBusyException or OcrTimeoutException or OcrDependencyUnavailableException
                    ? "ID photo processing is unavailable. Please try again."
                    : "We could not save your ID. Please select the photos again and try again.";
        }
        finally
        {
            if (!saved)
            {
                _db.ChangeTracker.Clear();
                if (!preserve && photos is not null)
                    await _scans.DeletePhotosAsync(user.Id, photos.Front, photos.Back);
            }
        }

        if (saved)
        {
            StatusMessage = "Your ID was added to your wallet.";
            if (WantsJson) return new JsonResult(new { success = true, redirectUrl = Url.Page("./MyIds") });
            return RedirectToPage();
        }
        ModelState.AddModelError("", ErrorMessage ?? "We could not save your ID. Please try again.");
        return await SaveFailureAsync(user, failureStatus, resetScan: true, reloadRequired: preserve);
    }

    private bool WantsJson => Request.Headers.Accept.Any(value =>
        value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    private async Task<IActionResult> SaveFailureAsync(ApplicationUser user, int status, bool resetScan, bool reloadRequired = false)
    {
        if (WantsJson)
        {
            var errors = ModelState.Where(pair => pair.Value?.Errors.Count > 0)
                // Empty/malformed posts can bind this complex model without its usual prefix.
                // Keep field-error keys stable so the existing form can display them in place.
                .ToDictionary(pair => pair.Key.Length == 0 || pair.Key == "Input" || pair.Key.StartsWith("Input.", StringComparison.Ordinal)
                    ? pair.Key : "Input." + pair.Key, pair => pair.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Check this field." : error.ErrorMessage).ToArray());
            var message = ErrorMessage ?? errors.Values.SelectMany(value => value).FirstOrDefault() ?? "Check the ID details and try again.";
            return new JsonResult(new { success = false, message, errors, resetScan, reloadRequired }) { StatusCode = status };
        }
        await LoadDocumentsAsync(user);
        return Page();
    }

    private async Task<bool?> VerifyCommitAsync(PatientIdDocument document)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // A serializable read waits for an unresolved insert even on databases using RCSI.
            // Only confirmed absence authorizes deletion of the already committed photo files.
            var found = false;
            await SqlReadRetry.RunIdempotentAsync(db, async () =>
            {
                await using var verificationScope = _scopes.CreateAsyncScope();
                var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await using var transaction = await verificationDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                found = await verificationDb.PatientIdDocuments.AsNoTracking().AnyAsync(d =>
                    d.Id == document.Id && d.PatientUserId == document.PatientUserId && d.FrontPhotoFileName == document.FrontPhotoFileName);
                await transaction.CommitAsync();
            });
            return found;
        }
        catch (Exception error)
        {
            _logger.LogWarning("ID document commit verification unavailable ({ErrorType}).", error.GetType().Name);
            return null;
        }
    }

    private async Task LoadDocumentsAsync(ApplicationUser user, int idPage = 1)
    {
        var query = _db.PatientIdDocuments.Where(document => document.PatientUserId == user.Id);
        var pagination = await PortalPagination.CreateAsync(query, _db, idPage, "idPage");
        ViewData["Pagination"] = pagination;
        Documents = await SqlReadRetry.RunAsync(_db, () => query
            .AsNoTracking()
            .Where(document => document.PatientUserId == user.Id)
            .OrderByDescending(document => document.CapturedAt)
            .ThenByDescending(document => document.Id).Skip(pagination.Offset).Take(PortalPagination.Size)
            .Select(document => new IdCardViewModel
            {
                Id = document.Id,
                IdType = document.IdType,
                IdNumber = document.IdNumber,
                CapturedAt = document.CapturedAt,
                HasFrontPhoto = document.FrontPhotoFileName != null,
                HasBackPhoto = document.BackPhotoFileName != null,
                IsManualEntry = document.IsManualEntry
            })
            .ToListAsync());

        if (pagination.Total == 0 && (!string.IsNullOrWhiteSpace(user.IdType) || !string.IsNullOrWhiteSpace(user.IdNumber)))
        {
            Documents = new[]
            {
                new IdCardViewModel
                {
                    IdType = string.IsNullOrWhiteSpace(user.IdType) ? "Government ID" : user.IdType,
                    IdNumber = user.IdNumber,
                    IsProfileOnly = true,
                    IsManualEntry = true
                }
            };
        }

        foreach (var document in Documents)
        {
            document.Issuer = PhilippineIdTypes.GetByName(document.IdType)?.Category ?? "Government-issued identification";
            document.MaskedIdNumber = MaskIdNumber(document.IdNumber);
        }

        await _auditLogService.LogAsync(
            user.Id, "VIEW_ID_WALLET", "Account/MyIds", "Viewed saved government ID details.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    }

    private static string MaskIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
        {
            return "Not provided";
        }

        var visibleCharacters = Math.Min(4, idNumber.Length);
        return new string('•', Math.Max(4, idNumber.Length - visibleCharacters)) + idNumber[^visibleCharacters..];
    }

    public sealed class IdCardViewModel
    {
        public int Id { get; init; }
        public string IdType { get; init; } = string.Empty;
        public string IdNumber { get; init; } = string.Empty;
        public string MaskedIdNumber { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public DateTime? CapturedAt { get; init; }
        public bool HasFrontPhoto { get; init; }
        public bool HasBackPhoto { get; init; }
        public bool IsManualEntry { get; init; }
        public bool IsProfileOnly { get; init; }
    }
}
