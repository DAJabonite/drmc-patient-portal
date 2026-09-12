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
using Microsoft.Extensions.Logging;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IIdDocumentExtractionService _idExtractionService;
        private readonly ApplicationDbContext _db;
        private readonly IPatientDocumentStorage _documentStorage;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IIdDocumentExtractionService idExtractionService,
            ApplicationDbContext db,
            IPatientDocumentStorage documentStorage)
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
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
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

            [Display(Name = "Date of birth")]
            [DataType(DataType.Date)]
            public DateTime? DateOfBirth { get; set; }

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
            [Display(Name = "Email address")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Enter your mobile number.")]
            [RegularExpression(@"^9\d{9}$", ErrorMessage = "Enter a 10-digit mobile number starting with 9.")]
            [Display(Name = "Mobile number")]
            public string Mobile { get; set; }

            [Required(ErrorMessage = "Select your valid government ID.")]
            [Display(Name = "Valid government ID")]
            public string IdType { get; set; }

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

            [Display(Name = "Data Privacy Consent (RA 10173)")]
            public bool PrivacyConsent { get; set; }

            // ID Photo Uploads & Tracking Tokens
            public IFormFile FrontPhoto { get; set; }
            public IFormFile BackPhoto { get; set; }
            public string TempFrontPhotoToken { get; set; }
            public string TempBackPhotoToken { get; set; }
            public float OcrConfidence { get; set; }
            public bool IsManualEntry { get; set; }
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
            try
            {
                stagedFront = await _documentStorage.StageAsync(frontPhoto, uploadSessionId, HttpContext.RequestAborted);
                if (backPhoto != null && backPhoto.Length > 0)
                {
                    stagedBack = await _documentStorage.StageAsync(backPhoto, uploadSessionId, HttpContext.RequestAborted);
                }

                // Execute genuine local OCR extraction
                await using var scanFrontStream = frontPhoto.OpenReadStream();
                using var scanBackStream = backPhoto?.OpenReadStream();
                var result = await _idExtractionService.ExtractFromStreamsAsync(idType, scanFrontStream, scanBackStream);

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
            catch (InvalidDataException ex)
            {
                await _documentStorage.DiscardStagedAsync(stagedFront?.Token, uploadSessionId, HttpContext.RequestAborted);
                await _documentStorage.DiscardStagedAsync(stagedBack?.Token, uploadSessionId, HttpContext.RequestAborted);
                return new JsonResult(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                await _documentStorage.DiscardStagedAsync(stagedFront?.Token, uploadSessionId, HttpContext.RequestAborted);
                await _documentStorage.DiscardStagedAsync(stagedBack?.Token, uploadSessionId, HttpContext.RequestAborted);
                _logger.LogError(ex, "Error processing ID photo extraction for {IdType}", idType);
                return new JsonResult(new { success = false, message = "Could not read ID photo. You can proceed with manual entry." });
            }
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            // Route a fresh registration straight to the patient dashboard.
            returnUrl ??= Url.Content("~/Patient/Home");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!Input.PrivacyConsent)
            {
                ModelState.AddModelError("Input.PrivacyConsent", "You must agree to the Data Privacy Consent to continue.");
            }

            if (ModelState.IsValid)
            {
                var user = CreateUser();
                user.FirstName = Input.FirstName?.Trim() ?? string.Empty;
                user.MiddleName = string.IsNullOrWhiteSpace(Input.MiddleName) ? null : Input.MiddleName.Trim();
                user.LastName = Input.LastName?.Trim() ?? string.Empty;
                user.FullName = string.IsNullOrWhiteSpace(user.MiddleName)
                    ? $"{user.FirstName} {user.LastName}"
                    : $"{user.FirstName} {user.MiddleName} {user.LastName}";
                user.ContactNumber = $"+63 {Input.Mobile?.Trim()}";
                user.PhoneNumber = Input.Mobile?.Trim();
                user.IdType = Input.IdType?.Trim() ?? string.Empty;
                user.IdNumber = Input.IdNumber?.Trim() ?? string.Empty;
                user.DateOfBirth = Input.DateOfBirth;
                user.Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address.Trim();
                user.Sex = string.IsNullOrWhiteSpace(Input.Sex) ? null : Input.Sex.Trim();
                user.BloodType = string.IsNullOrWhiteSpace(Input.BloodType) ? null : Input.BloodType.Trim();
                user.PrivacyConsent = Input.PrivacyConsent;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    try
                    {
                        await SavePatientIdDocumentAsync(user);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist PatientIdDocument for user {UserId}", user.Id);
                        await _userManager.DeleteAsync(user);
                        ModelState.AddModelError(string.Empty, "We could not securely save the identity document. Please upload it again.");
                        return Page();
                    }

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private async Task SavePatientIdDocumentAsync(ApplicationUser user)
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
                    IdType = Input.IdType?.Trim() ?? string.Empty,
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
                        DateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                        user.Address,
                        user.Sex,
                        user.BloodType,
                        user.IdNumber
                    })
                };

                _db.PatientIdDocuments.Add(doc);
                await _db.SaveChangesAsync();
            }
            catch
            {
                await _documentStorage.DeleteStoredAsync(user.Id, frontFileName, CancellationToken.None);
                await _documentStorage.DeleteStoredAsync(user.Id, backFileName, CancellationToken.None);
                throw;
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
