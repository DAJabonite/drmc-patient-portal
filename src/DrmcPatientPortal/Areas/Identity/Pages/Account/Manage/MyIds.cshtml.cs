using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class MyIdsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogService _auditLogService;

    public MyIdsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditLogService auditLogService)
    {
        _db = db;
        _userManager = userManager;
        _auditLogService = auditLogService;
    }

    public IReadOnlyList<IdCardViewModel> Documents { get; private set; } = Array.Empty<IdCardViewModel>();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        Documents = await _db.PatientIdDocuments
            .AsNoTracking()
            .Where(document => document.PatientUserId == user.Id)
            .OrderByDescending(document => document.CapturedAt)
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
            .ToListAsync();

        if (Documents.Count == 0 && (!string.IsNullOrWhiteSpace(user.IdType) || !string.IsNullOrWhiteSpace(user.IdNumber)))
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
            user.Id,
            "VIEW_ID_WALLET",
            "Account/MyIds",
            "Viewed saved government ID details.",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        return Page();
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
