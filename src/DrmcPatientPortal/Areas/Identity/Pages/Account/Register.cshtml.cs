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
        private readonly IWebHostEnvironment _environment;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            IIdDocumentExtractionService idExtractionService,
            ApplicationDbContext db,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _idExtractionService = idExtractionService;
            _db = db;
            _environment = environment;
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

            try
            {
                // Save temporary photos in App_Data/TempUploads/ for later persistence upon account creation
                string tempDir = Path.Combine(_environment.ContentRootPath, "App_Data", "TempUploads");
                Directory.CreateDirectory(tempDir);

                string frontToken = $"{Guid.NewGuid():N}_{Path.GetFileName(frontPhoto.FileName)}";
                string frontTempPath = Path.Combine(tempDir, frontToken);
                await using (var frontStream = new FileStream(frontTempPath, FileMode.Create))
                {
                    await frontPhoto.CopyToAsync(frontStream);
                }

                string backToken = null;
                if (backPhoto != null && backPhoto.Length > 0)
                {
                    backToken = $"{Guid.NewGuid():N}_{Path.GetFileName(backPhoto.FileName)}";
                    string backTempPath = Path.Combine(tempDir, backToken);
                    await using var backStream = new FileStream(backTempPath, FileMode.Create);
                    await backPhoto.CopyToAsync(backStream);
                }

                // Execute genuine local OCR extraction
                await using var scanFrontStream = frontPhoto.OpenReadStream();
                using var scanBackStream = backPhoto?.OpenReadStream();
                var result = await _idExtractionService.ExtractFromStreamsAsync(idType, scanFrontStream, scanBackStream);

                return new JsonResult(new
                {
                    success = result.Success,
                    meanConfidence = result.MeanConfidence,
                    tempFrontToken = frontToken,
                    tempBackToken = backToken,
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
            catch (Exception ex)
            {
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

                    // Persist PatientIdDocument outside wwwroot
                    await SavePatientIdDocumentAsync(user);

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
            try
            {
                string patientStorageDir = Path.Combine(_environment.ContentRootPath, "App_Data", "PatientIdDocuments", user.Id);
                Directory.CreateDirectory(patientStorageDir);

                string frontFileName = null;
                string backFileName = null;

                // 1. Direct form file upload if present
                if (Input.FrontPhoto != null && Input.FrontPhoto.Length > 0)
                {
                    frontFileName = $"front_{Guid.NewGuid():N}{Path.GetExtension(Input.FrontPhoto.FileName)}";
                    string frontPath = Path.Combine(patientStorageDir, frontFileName);
                    await using var stream = new FileStream(frontPath, FileMode.Create);
                    await Input.FrontPhoto.CopyToAsync(stream);
                }
                // 2. Transfer from temp token if uploaded during AJAX scan step
                else if (!string.IsNullOrWhiteSpace(Input.TempFrontPhotoToken))
                {
                    string tempDir = Path.Combine(_environment.ContentRootPath, "App_Data", "TempUploads");
                    string tempPath = Path.Combine(tempDir, Input.TempFrontPhotoToken);
                    if (System.IO.File.Exists(tempPath))
                    {
                        frontFileName = $"front_{Guid.NewGuid():N}{Path.GetExtension(Input.TempFrontPhotoToken)}";
                        string destPath = Path.Combine(patientStorageDir, frontFileName);
                        System.IO.File.Move(tempPath, destPath);
                    }
                }

                if (Input.BackPhoto != null && Input.BackPhoto.Length > 0)
                {
                    backFileName = $"back_{Guid.NewGuid():N}{Path.GetExtension(Input.BackPhoto.FileName)}";
                    string backPath = Path.Combine(patientStorageDir, backFileName);
                    await using var stream = new FileStream(backPath, FileMode.Create);
                    await Input.BackPhoto.CopyToAsync(stream);
                }
                else if (!string.IsNullOrWhiteSpace(Input.TempBackPhotoToken))
                {
                    string tempDir = Path.Combine(_environment.ContentRootPath, "App_Data", "TempUploads");
                    string tempPath = Path.Combine(tempDir, Input.TempBackPhotoToken);
                    if (System.IO.File.Exists(tempPath))
                    {
                        backFileName = $"back_{Guid.NewGuid():N}{Path.GetExtension(Input.TempBackPhotoToken)}";
                        string destPath = Path.Combine(patientStorageDir, backFileName);
                        System.IO.File.Move(tempPath, destPath);
                    }
                }

                var doc = new PatientIdDocument
                {
                    PatientUserId = user.Id,
                    IdType = Input.IdType?.Trim() ?? string.Empty,
                    IdNumber = Input.IdNumber?.Trim() ?? string.Empty,
                    FrontPhotoFileName = frontFileName,
                    BackPhotoFileName = backFileName,
                    CapturedAt = DateTime.UtcNow,
                    OcrConfidence = Input.OcrConfidence,
                    IsManualEntry = Input.IsManualEntry || (frontFileName == null),
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist PatientIdDocument for user {UserId}", user.Id);
            }
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
