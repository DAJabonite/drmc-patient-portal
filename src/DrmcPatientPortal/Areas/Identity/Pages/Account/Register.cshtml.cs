// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IAccountEmailQueue _emailSender;
        private readonly AccountLinks _links;
        private readonly IIdDocumentExtractionService _idExtractionService;
        private readonly ApplicationDbContext _db;
        private readonly IPatientDocumentStorage _documentStorage;
        private readonly IAuditLogService _auditLog;
        private readonly PatientRegistrationOptions _registrationOptions;
        private readonly FirstAdminSignup _firstAdminSignup;
        private readonly IServiceScopeFactory _scopes;

        // One message for every code failure (unknown, used, revoked, expired, already linked or
        // name or birth date mismatch), so the form never reveals which part was wrong.
        public const string HospitalRecordCodeError =
            "We could not verify your hospital record. Check the code, your name, and your date of birth, or ask the PACD or your clinic for a new code.";

        // One message for every invitation failure (unknown, used, revoked, expired or a different
        // email address).
        public const string StaffInvitationError =
            "We could not verify this staff invitation. Use the email address the invitation was sent to, or ask a portal Admin for a new invitation.";

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IAccountEmailQueue emailSender,
            IIdDocumentExtractionService idExtractionService,
            ApplicationDbContext db,
            IPatientDocumentStorage documentStorage,
            IAuditLogService auditLog,
            IOptions<PatientRegistrationOptions> registrationOptions,
            FirstAdminSignup firstAdminSignup,
            AccountLinks links,
            IServiceScopeFactory scopes)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _idExtractionService = idExtractionService;
            _db = db;
            _documentStorage = documentStorage;
            _auditLog = auditLog;
            _registrationOptions = registrationOptions.Value;
            _firstAdminSignup = firstAdminSignup;
            _links = links;
            _scopes = scopes;
        }

        public bool RequireHospitalRecordCode => _registrationOptions.RequireHospitalRecordCode;

        // Staff sign up with an Admin invitation instead of a hospital record code.
        public bool StaffInvitationMode => !string.IsNullOrWhiteSpace(Input?.StaffInvitationCode);

        // While first-Admin setup is open, the browser cannot know which email is exempt, so the
        // code is checked only on the server (it stays required for every other email).
        public bool FirstAdminSetupOpen { get; private set; }

        public bool RequireCodeInBrowser => RequireHospitalRecordCode && !StaffInvitationMode && !FirstAdminSetupOpen;

        public override async Task OnPageHandlerExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context,
            Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutionDelegate next)
        {
            FirstAdminSetupOpen = RequireHospitalRecordCode && await _firstAdminSignup.IsOpenAsync(HttpContext.RequestAborted);
            await next();
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel : IValidatableObject
        {
            [Required(ErrorMessage = "Enter your first name.")]
            [StringLength(60, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "First name")]
            public string FirstName { get; set; }

            [StringLength(60, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Middle name (optional)")]
            public string MiddleName { get; set; }

            [Required(ErrorMessage = "Enter your last name.")]
            [StringLength(60, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Last name")]
            public string LastName { get; set; }

            [StringLength(10, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Suffix (optional)")]
            public string Suffix { get; set; }

            [Display(Name = "Date of birth")]
            [DataType(DataType.Date)]
            public DateTime? DateOfBirth { get; set; }

            [Required(ErrorMessage = "Enter your residential address.")]
            [StringLength(200, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Complete address")]
            public string Address { get; set; }

            [StringLength(20, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Sex")]
            public string Sex { get; set; }

            [StringLength(10, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "Blood type")]
            public string BloodType { get; set; }

            [Required(ErrorMessage = "Enter your email address.")]
            [EmailAddress(ErrorMessage = "Enter a valid email address.")]
            [StringLength(256)]
            [Display(Name = "Email address")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Enter your mobile number.")]
            [RegularExpression(@"^9\d{9}$", ErrorMessage = "Enter a 10-digit mobile number starting with 9.")]
            [Display(Name = "Mobile number")]
            public string Mobile { get; set; }

            [Required(ErrorMessage = "Select your valid government ID.")]
            [Display(Name = "Valid government ID")]
            public string IdType { get; set; }

            [StringLength(120)]
            public string OtherGovernmentIdName { get; set; }

            [Required(ErrorMessage = "Enter your ID number.")]
            [StringLength(40, ErrorMessage = "The {0} must be at most {1} characters long.")]
            [Display(Name = "ID number")]
            public string IdNumber { get; set; }

            [Required(ErrorMessage = "Create a password.")]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [Required(ErrorMessage = "Confirm your password.")]
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "Passwords do not match.")]
            public string ConfirmPassword { get; set; }

            [StringLength(64, ErrorMessage = HospitalRecordCodeError)]
            [Display(Name = "Hospital record code")]
            public string HospitalRecordCode { get; set; }

            [StringLength(64, ErrorMessage = "Enter the invitation code exactly as sent (15 or 20 characters).")]
            [Display(Name = "Staff invitation code")]
            public string StaffInvitationCode { get; set; }

            [Display(Name = "Data Privacy Consent (RA 10173)")]
            public bool PrivacyConsent { get; set; }

            // ID Photo Uploads & Tracking Tokens
            public IFormFile FrontPhoto { get; set; }
            public IFormFile BackPhoto { get; set; }
            public string TempFrontPhotoToken { get; set; }
            public string TempBackPhotoToken { get; set; }
            public float OcrConfidence { get; set; }
            public bool IsManualEntry { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (DateOfBirth?.Date > ClinicalClock.Today)
                {
                    yield return new ValidationResult("Date of birth cannot be in the future.", new[] { nameof(DateOfBirth) });
                }
                if (!float.IsFinite(OcrConfidence))
                {
                    yield return new ValidationResult("Enter a finite OCR confidence value.", new[] { nameof(OcrConfidence) });
                }
            }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        // AJAX Handler for Real Local OCR Scan
        public async Task<IActionResult> OnPostExtractIdAsync(string idType, IFormFile frontPhoto, IFormFile backPhoto)
        {
            if (string.IsNullOrWhiteSpace(idType) || frontPhoto == null || frontPhoto.Length == 0)
            {
                return new JsonResult(new { success = false, message = "Please select an ID type and provide a clear front photo." });
            }

            StagedPatientDocument stagedFront = null;
            StagedPatientDocument stagedBack = null;
            var uploadSessionId = GetUploadSessionId();
            var retainStaged = false;
            try
            {
                stagedFront = await _documentStorage.StageAsync(frontPhoto, uploadSessionId, HttpContext.RequestAborted);
                if (backPhoto != null && backPhoto.Length > 0)
                {
                    stagedBack = await _documentStorage.StageAsync(backPhoto, uploadSessionId, HttpContext.RequestAborted);
                }

                // Execute genuine local OCR extraction
                await using var scanFrontStream = frontPhoto.OpenReadStream();
                using var scanBackStream = backPhoto is { Length: > 0 } ? backPhoto.OpenReadStream() : null;
                var result = await _idExtractionService.ExtractFromStreamsAsync(idType, scanFrontStream, scanBackStream, HttpContext.RequestAborted);

                retainStaged = true;
                return new JsonResult(new
                {
                    success = result.Success,
                    meanConfidence = result.MeanConfidence,
                    tempFrontToken = stagedFront.Token,
                    tempBackToken = stagedBack?.Token,
                    firstName = result.FirstName.Found ? result.FirstName.Value : "",
                    middleName = result.MiddleName.Found ? result.MiddleName.Value : "",
                    lastName = result.LastName.Found ? result.LastName.Value : "",
                    fullName = result.FullName.Found ? result.FullName.Value : "",
                    idNumber = result.IdNumber.Found ? result.IdNumber.Value : "",
                    dateOfBirth = result.DateOfBirth.Found ? result.DateOfBirth.Value.ToString("yyyy-MM-dd") : "",
                    address = result.Address.Found ? result.Address.Value : "",
                    sex = result.Sex.Found ? result.Sex.Value : "",
                    bloodType = result.BloodType.Found ? result.BloodType.Value : "",
                    foundSummary = result.FoundFieldsSummary
                });
            }
            catch (OcrBusyException ex)
            {
                Response.Headers.RetryAfter = "5";
                return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 429 };
            }
            catch (OcrTimeoutException ex)
            {
                return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 503 };
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                return StatusCode(499);
            }
            catch (OcrDependencyUnavailableException ex)
            {
                _logger.LogError(ex, "ID scanning dependencies unavailable.");
                return new JsonResult(new { success = false, message = "ID scanning is temporarily unavailable. Please try again later." }) { StatusCode = 503 };
            }
            catch (InvalidDataException ex)
            {
                return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 400 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ID photo extraction for {IdType}", idType);
                return new JsonResult(new { success = false, message = "Could not read ID photo. You can proceed with manual entry." });
            }
            finally
            {
                if (!retainStaged)
                {
                    foreach (var token in new[] { stagedFront?.Token, stagedBack?.Token })
                    {
                        try { await _documentStorage.DiscardStagedAsync(token, uploadSessionId, CancellationToken.None); }
                        catch (Exception) { _logger.LogWarning("Could not discard a staged ID photo."); }
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            // Route a fresh registration straight to the patient dashboard.
            returnUrl ??= Url.Content("~/Patient/Home");
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!Input.PrivacyConsent)
            {
                ModelState.AddModelError("Input.PrivacyConsent", "Read and accept the privacy notice and portal terms to continue.");
            }

            var selectedId = PhilippineIdTypes.GetByName(Input.IdType);
            if (selectedId is null)
                ModelState.AddModelError("Input.IdType", "Select a government-issued ID, or choose Other government-issued ID.");
            else
                Input.IdType = selectedId.Name;
            if (Input.IdType == PhilippineIdTypes.OtherGovernment && string.IsNullOrWhiteSpace(Input.OtherGovernmentIdName))
                ModelState.AddModelError("Input.OtherGovernmentIdName", "Enter the ID name and government issuer.");

            StaffInvitation invitation = null;
            if (StaffInvitationMode)
            {
                var canonicalInvite = HospitalRecordCodes.Normalize(Input.StaffInvitationCode);
                if (canonicalInvite is null)
                    ModelState.AddModelError("Input.StaffInvitationCode", "Enter the invitation code exactly as sent (15 or 20 characters).");
                else if (ModelState.IsValid)
                {
                    invitation = await FindAcceptableInvitationAsync(canonicalInvite, Input.Email);
                    if (invitation is null) ModelState.AddModelError("Input.StaffInvitationCode", StaffInvitationError);
                }
            }

            PatientRegistrationCode recordCode = null;
            var canonicalCode = HospitalRecordCodes.Normalize(Input.HospitalRecordCode);
            if (string.IsNullOrWhiteSpace(Input.HospitalRecordCode))
            {
                // A staff invitation or first-Admin setup replaces the hospital record code.
                var exempt = StaffInvitationMode || await _firstAdminSignup.AllowsAsync(Input.Email, HttpContext.RequestAborted);
                if (RequireHospitalRecordCode && !exempt)
                {
                    await RejectRecordCodeAsync();
                    ModelState.AddModelError("Input.HospitalRecordCode", HospitalRecordCodeError);
                }
            }
            else if (canonicalCode is null)
            {
                await RejectRecordCodeAsync();
                if (!ModelState.TryGetValue("Input.HospitalRecordCode", out var codeState) || codeState.Errors.Count == 0)
                    ModelState.AddModelError("Input.HospitalRecordCode", HospitalRecordCodeError);
            }
            else if (ModelState.IsValid || new[] { "Input.LastName", "Input.FirstName", "Input.MiddleName", "Input.Suffix", "Input.DateOfBirth" }
                .Any(field => ModelState.TryGetValue(field, out var state) && state.Errors.Count > 0))
            {
                recordCode = await FindRedeemableCodeAsync(canonicalCode, Input.DateOfBirth);
                if (recordCode is null) ModelState.AddModelError("Input.HospitalRecordCode", HospitalRecordCodeError);
            }

            if (ModelState.IsValid)
            {
                var user = CreateUser();
                user.FirstName = PatientNames.Clean(Input.FirstName);
                user.MiddleName = string.IsNullOrWhiteSpace(Input.MiddleName) ? null : PatientNames.Clean(Input.MiddleName);
                user.LastName = PatientNames.Clean(Input.LastName);
                user.Suffix = string.IsNullOrWhiteSpace(Input.Suffix) ? null : PatientNames.Clean(Input.Suffix);
                user.FullName = string.Join(' ', new[] { user.FirstName, user.MiddleName, user.LastName, user.Suffix }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
                user.ContactNumber = $"+63 {Input.Mobile?.Trim()}";
                user.PhoneNumber = Input.Mobile?.Trim();
                user.IdType = Input.IdType == PhilippineIdTypes.OtherGovernment
                    ? Input.OtherGovernmentIdName!.Trim() : Input.IdType.Trim();
                user.IdNumber = Input.IdNumber?.Trim() ?? string.Empty;
                user.DateOfBirth = Input.DateOfBirth;
                user.Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address.Trim();
                user.Sex = string.IsNullOrWhiteSpace(Input.Sex) ? null : Input.Sex.Trim();
                user.BloodType = string.IsNullOrWhiteSpace(Input.BloodType) ? null : Input.BloodType.Trim();
                user.PrivacyConsent = Input.PrivacyConsent;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                return await CompleteRegistrationAsync(user, recordCode, invitation, returnUrl);
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private async Task<IActionResult> CompleteRegistrationAsync(ApplicationUser user, PatientRegistrationCode recordCode,
            StaffInvitation invitation, string returnUrl)
        {
            var messageId = Guid.NewGuid();
            PatientIdDocument document = null;
            IDbContextTransaction transaction = null;
            var committed = false;
            var preserveFiles = false;
            var acknowledgementLost = false;
            try
            {
                // Identity, claims, document metadata, audit and verification email become visible together.
                // Registration is never replayed after an uncertain commit.
                transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    return Page();
                }

                if (recordCode is not null && !await RedeemCodeAsync(recordCode, user.Id))
                {
                    ModelState.AddModelError("Input.HospitalRecordCode", HospitalRecordCodeError);
                    return Page();
                }

                if (invitation is not null && !await AcceptInvitationAsync(invitation.Id, user.Id))
                {
                    ModelState.AddModelError("Input.StaffInvitationCode", StaffInvitationError);
                    return Page();
                }

                try { document = await SavePatientIdDocumentAsync(user); }
                catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    _logger.LogError("Signup identity document could not be saved ({ErrorType}).", error.GetType().Name);
                    ModelState.AddModelError(string.Empty, "We could not securely save the identity document. Please upload it again.");
                    return Page();
                }

                if (recordCode is not null)
                {
                    await _auditLog.LogAsync(user.Id, "LINK_HOSPITAL_RECORD", $"PatientRecord/{recordCode.PatientRecordId}",
                        $"Hospital record linked at signup with a code issued by {recordCode.IssuingPoint}.",
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                }

                if (invitation is not null)
                {
                    await _auditLog.LogAsync(user.Id, "ACCEPT_STAFF_INVITATION", $"StaffInvitation/{invitation.Id}",
                        "Staff account created with an Admin invitation. The role is granted after email confirmation and two-factor authentication.",
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                }
                else if (recordCode is null && RequireHospitalRecordCode)
                {
                    _logger.LogWarning("First Admin account created without a hospital record code while AdminBootstrap is enabled.");
                    await _auditLog.LogAsync(user.Id, "FIRST_ADMIN_SIGNUP", "AdminBootstrap",
                        "Account created without a hospital record code during first-Admin setup.",
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                }

                var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = _links.Page(HttpContext, "/Account/ConfirmEmail",
                    new { area = "Identity", userId = user.Id, code, returnUrl });

                _emailSender.Stage(_db, messageId, user.Id, Input.Email, "Confirm your email",
                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
                await _db.SaveChangesAsync(HttpContext.RequestAborted);

                try
                {
                    await transaction.CommitAsync(CancellationToken.None);
                    committed = true;
                }
                catch (Exception error)
                {
                    // A lost acknowledgement can follow a successful commit. Preserve files until an
                    // independent SQL read proves whether this exact transaction's message exists.
                    preserveFiles = true;
                    acknowledgementLost = true;
                    _logger.LogWarning("Signup commit acknowledgement unavailable ({ErrorType}).", error.GetType().Name);
                    await transaction.DisposeAsync();
                    transaction = null;
                    var outcome = await VerifyRegistrationCommitAsync(messageId, user.Id);
                    committed = outcome == true;
                    preserveFiles = outcome is null;
                    if (!committed)
                    {
                        ModelState.AddModelError(string.Empty, preserveFiles ? UncertainSignupMessage : FailedSignupMessage);
                        return Page();
                    }
                }
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                _logger.LogError("Signup could not be completed ({ErrorType}).", error.GetType().Name);
                ModelState.AddModelError(string.Empty, preserveFiles ? UncertainSignupMessage : FailedSignupMessage);
                return Page();
            }
            finally
            {
                if (transaction is not null)
                {
                    try
                    {
                        if (!committed) await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { _logger.LogWarning("Signup rollback unavailable ({ErrorType}).", error.GetType().Name); }
                    finally
                    {
                        try { await transaction.DisposeAsync(); }
                        catch (Exception error) { _logger.LogWarning("Signup transaction disposal unavailable ({ErrorType}).", error.GetType().Name); }
                    }
                }
                if (!committed)
                {
                    _db.ChangeTracker.Clear();
                    if (!preserveFiles && document is not null)
                        await DeleteRegistrationFilesAsync(user.Id, document.FrontPhotoFileName, document.BackPhotoFileName);
                    // This audit must survive rollback, without retaining the rolled-back account ID.
                    // An uncertain commit is not classified as a rejected link.
                    if (!preserveFiles && recordCode is not null)
                    {
                        await using var auditScope = _scopes.CreateAsyncScope();
                        await HospitalRecordLinkAudit.RejectedAsync(auditScope.ServiceProvider.GetRequiredService<IAuditLogService>(),
                            HttpContext, "registration_rejected");
                    }
                }
            }

            _logger.LogInformation("User created a new account with password.");
            if (_userManager.Options.SignIn.RequireConfirmedAccount)
                return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
            if (acknowledgementLost) return RedirectToPage("Login", new { returnUrl });
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        private const string FailedSignupMessage = "We could not complete signup. Please try again and upload any identity photos again.";
        private const string UncertainSignupMessage = "We could not confirm whether signup completed. Try signing in, or use Resend confirmation before trying again.";

        private async Task<bool?> VerifyRegistrationCommitAsync(Guid messageId, string userId)
        {
            try
            {
                await using var strategyScope = _scopes.CreateAsyncScope();
                var strategyDb = strategyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var committed = false;
                await SqlReadRetry.RunIdempotentAsync(strategyDb, async () =>
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    // Lock the stable marker key even when READ_COMMITTED_SNAPSHOT is enabled.
                    // An unresolved insert must finish before absence can authorize file cleanup.
                    await using var verification = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                    var found = await db.AccountEmails.AsNoTracking().AnyAsync(m => m.Id == messageId && m.UserId == userId);
                    await verification.CommitAsync();
                    committed = found;
                });
                return committed;
            }
            catch (Exception error)
            {
                _logger.LogWarning("Signup commit could not be verified ({ErrorType}).", error.GetType().Name);
                return null;
            }
        }

        private async Task DeleteRegistrationFilesAsync(string userId, params string[] fileNames)
        {
            foreach (var fileName in fileNames.Where(name => !string.IsNullOrEmpty(name)).Distinct())
            {
                try { await _documentStorage.DeleteStoredAsync(userId, fileName, CancellationToken.None); }
                catch (Exception error) { _logger.LogWarning("Signup identity photo cleanup unavailable ({ErrorType}).", error.GetType().Name); }
            }
        }

        private async Task<PatientIdDocument> SavePatientIdDocumentAsync(ApplicationUser user)
        {
            string frontFileName = null;
            string backFileName = null;
            string frontContentType = null;
            string backContentType = null;
            var storageVersion = 2;

            try
            {
                if (Input.FrontPhoto != null && Input.FrontPhoto.Length > 0)
                {
                    var stored = await _documentStorage.StoreAsync(Input.FrontPhoto, user.Id, "front", HttpContext.RequestAborted);
                    frontFileName = stored.FileName;
                    frontContentType = stored.ContentType;
                    storageVersion = stored.StorageVersion;
                }
                else if (!string.IsNullOrWhiteSpace(Input.TempFrontPhotoToken))
                {
                    var stored = await _documentStorage.CommitAsync(Input.TempFrontPhotoToken, GetUploadSessionId(), user.Id, "front", HttpContext.RequestAborted);
                    frontFileName = stored.FileName;
                    frontContentType = stored.ContentType;
                    storageVersion = stored.StorageVersion;
                }

                if (Input.BackPhoto != null && Input.BackPhoto.Length > 0)
                {
                    var stored = await _documentStorage.StoreAsync(Input.BackPhoto, user.Id, "back", HttpContext.RequestAborted);
                    backFileName = stored.FileName;
                    backContentType = stored.ContentType;
                }
                else if (!string.IsNullOrWhiteSpace(Input.TempBackPhotoToken))
                {
                    var stored = await _documentStorage.CommitAsync(Input.TempBackPhotoToken, GetUploadSessionId(), user.Id, "back", HttpContext.RequestAborted);
                    backFileName = stored.FileName;
                    backContentType = stored.ContentType;
                }

                var doc = new PatientIdDocument
                {
                    PatientUserId = user.Id,
                    IdType = user.IdType,
                    IdNumber = Input.IdNumber?.Trim() ?? string.Empty,
                    FrontPhotoFileName = frontFileName,
                    BackPhotoFileName = backFileName,
                    FrontPhotoContentType = frontContentType,
                    BackPhotoContentType = backContentType,
                    StorageVersion = storageVersion,
                    CapturedAt = DateTime.UtcNow,
                    OcrConfidence = Input.OcrConfidence,
                    IsManualEntry = Input.IsManualEntry || frontFileName == null,
                    ExtractedFieldsJson = JsonSerializer.Serialize(new
                    {
                        user.FirstName,
                        user.MiddleName,
                        user.LastName,
                        user.Suffix,
                        DateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                        user.Address,
                        user.Sex,
                        user.BloodType,
                        user.IdNumber
                    })
                };

                _db.PatientIdDocuments.Add(doc);
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
                return doc;
            }
            catch
            {
                await DeleteRegistrationFilesAsync(user.Id, frontFileName, backFileName);
                throw;
            }
        }

        // A code is redeemable when it is active, its record is still unlinked and the record's birth
        // date and structured name match the values entered at signup.
        private async Task<PatientRegistrationCode> FindRedeemableCodeAsync(string canonicalCode, DateTime? dateOfBirth)
        {
            var hash = HospitalRecordCodes.Hash(canonicalCode);
            var now = DateTime.UtcNow;
            var code = await _db.PatientRegistrationCodes.AsNoTracking().Include(c => c.PatientRecord)
                .Where(c => c.CodeHash == hash)
                .SingleOrDefaultAsync(HttpContext.RequestAborted);
            if (code is null || code.StatusAt(now) != RegistrationCodeStatus.Active || code.PatientRecord.PortalUserId is not null)
            {
                await RejectRecordCodeAsync();
                return null;
            }
            if (!code.PatientRecord.HasHospitalNumber)
            {
                await RejectRecordCodeAsync();
                return null;
            }
            if (HospitalRecordCodes.MatchesIdentity(code.PatientRecord, Input.LastName, Input.FirstName, dateOfBirth, Input.MiddleName, Input.Suffix))
                return code;
            await HospitalRecordCodes.RejectIdentityMismatchAsync(_db, _auditLog, HttpContext, code.Id, "registration_rejected");
            return null;
        }

        private Task RejectRecordCodeAsync() =>
            HospitalRecordLinkAudit.RejectedAsync(_auditLog, HttpContext, "registration_rejected");

        // Marks the code used and links the record inside the signup transaction. Conditional
        // updates make a concurrent second redemption or a revoke in between fail closed.
        private async Task<bool> RedeemCodeAsync(PatientRegistrationCode code, string userId)
        {
            var now = DateTime.UtcNow;
            try
            {
                var redeemed = await _db.PatientRegistrationCodes
                    .Where(c => c.Id == code.Id && c.RowVersion == code.RowVersion && c.RedeemedAtUtc == null &&
                        c.RevokedAtUtc == null && c.LockedAtUtc == null && c.FailedAttemptCount < PatientRegistrationCode.MaximumFailedAttempts && c.ExpiresAtUtc > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedeemedAtUtc, now).SetProperty(c => c.RedeemedByUserId, userId), CancellationToken.None);
                var linked = redeemed == 1 && await _db.PatientRecords
                    .Where(p => p.Id == code.PatientRecordId && p.RowVersion == code.PatientRecord.RowVersion && p.PortalUserId == null &&
                        p.DateOfBirth != null && p.DateOfBirth.Value.Date == code.PatientRecord.DateOfBirth.Value.Date)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.PortalUserId, userId), CancellationToken.None) == 1;
                return linked;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
            {
                _logger.LogError("Hospital record registration could not be completed.");
                return false;
            }
        }

        // An invitation is acceptable when it is unused, not revoked, not expired and was sent to the
        // email address used for this signup.
        private async Task<StaffInvitation> FindAcceptableInvitationAsync(string canonicalCode, string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var hash = HospitalRecordCodes.Hash(canonicalCode);
            var normalized = _userManager.NormalizeEmail(email.Trim());
            var now = DateTime.UtcNow;
            return await _db.StaffInvitations.AsNoTracking()
                .Where(i => i.CodeHash == hash && i.NormalizedEmail == normalized && i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
                .SingleOrDefaultAsync(HttpContext.RequestAborted);
        }

        // Conditional update: a concurrent second signup or a revoke in between fails closed.
        private async Task<bool> AcceptInvitationAsync(int invitationId, string userId)
        {
            var now = DateTime.UtcNow;
            try
            {
                return await _db.StaffInvitations
                    .Where(i => i.Id == invitationId && i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAtUtc, now).SetProperty(i => i.AcceptedByUserId, userId), CancellationToken.None) == 1;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
            {
                _logger.LogError(ex, "Staff invitation {InvitationId} could not be accepted", invitationId);
                return false;
            }
        }

        private string GetUploadSessionId()
        {
            const string key = "RegistrationUploadSessionId";
            var value = HttpContext.Session.GetString(key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(key, value);
            return value;
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
