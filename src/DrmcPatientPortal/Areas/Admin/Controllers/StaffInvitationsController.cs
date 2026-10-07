using System.Globalization;
using System.Text.Encodings.Web;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

// Admin-only (not in the staff policy registry). Invites a DRMC employee to create a staff account
// without a hospital record code. The invitation is bound to one email and one grantable role;
// it never grants Admin. The role is added after the new account confirms its email and enables
// two-factor authentication.
[Area("Admin")]
public sealed class StaffInvitationsController(ApplicationDbContext db, AdminWrites writes, IQrCodeService qr, IEmailSender email,
    ILookupNormalizer normalizer, IOptions<StaffInvitationOptions> options, ILogger<StaffInvitationsController> logger) : Controller
{
    private const string Entity = "StaffInvitations";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 256) { ModelState.AddModelError(nameof(search), "Search must be at most 256 characters."); search = null; }
        var query = db.StaffInvitations.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(i => EF.Functions.Collate(i.Email, "Latin1_General_100_CI_AS").Contains(search) || i.CodeHint == search.ToUpper());
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var invitations = await query.OrderByDescending(i => i.Id).Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var rows = invitations.Select(i => new StaffInvitationRow(i.Id, i.Email, i.Role, i.CodeHint, i.InvitedByEmail, Local(i.CreatedAtUtc), Local(i.ExpiresAtUtc),
            i.AcceptedAtUtc is { } a ? Local(a) : null, i.StatusAt(now), Convert.ToBase64String(i.RowVersion))).ToArray();
        return View(new StaffInvitationList(rows, search, page, total));
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["LifetimeDays"] = (int)options.Value.Lifetime.TotalDays;
        return View(new StaffInvitationInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(StaffInvitationInput input, CancellationToken cancellationToken)
    {
        input.Email = input.Email?.Trim() ?? string.Empty;
        if (!StaffRoles.Grantable.Contains(input.Role)) ModelState.AddModelError(nameof(input.Role), "Select the staff role to grant.");
        var normalized = normalizer.NormalizeEmail(input.Email);
        if (ModelState.IsValid && await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
            ModelState.AddModelError(nameof(input.Email), "An account with this email already exists. Grant the role in Staff access instead.");
        if (!ModelState.IsValid)
        {
            ViewData["LifetimeDays"] = (int)options.Value.Lifetime.TotalDays;
            return View(input);
        }

        string? code = null;
        DateTime expires = default;
        var result = await writes.RunAsync(async database =>
        {
            var now = DateTime.UtcNow;
            // One open invitation per email: a new invitation replaces any earlier unused one.
            var open = await database.StaffInvitations.Where(i => i.NormalizedEmail == normalized && i.AcceptedAtUtc == null &&
                i.RevokedAtUtc == null && i.ExpiresAtUtc > now).ToListAsync(cancellationToken);
            foreach (var earlier in open) earlier.RevokedAtUtc = now;
            var canonical = HospitalRecordCodes.Normalize(HospitalRecordCodes.Generate())!;
            var actor = await database.Users.AsNoTracking().Where(u => u.Id == ActorId).Select(u => new { u.Id, u.Email }).SingleAsync(cancellationToken);
            var entity = new StaffInvitation
            {
                Email = input.Email, NormalizedEmail = normalized, Role = input.Role,
                CodeHash = HospitalRecordCodes.Hash(canonical), CodeHint = HospitalRecordCodes.Hint(canonical),
                InvitedById = actor.Id, InvitedByEmail = actor.Email ?? string.Empty, CreatedAtUtc = now, ExpiresAtUtc = now + options.Value.Lifetime,
            };
            database.StaffInvitations.Add(entity);
            await database.SaveChangesAsync(cancellationToken);
            foreach (var earlier in open)
                await AdminAudit.AddAsync(database, User, "Revoke", Entity, earlier.Id.ToString(CultureInfo.InvariantCulture),
                    fields: [nameof(StaffInvitation.RevokedAtUtc)], cancellationToken: cancellationToken);
            await AdminAudit.AddAsync(database, User, "Invite", Entity, entity.Id.ToString(CultureInfo.InvariantCulture),
                fields: [nameof(StaffInvitation.Email), nameof(StaffInvitation.Role), nameof(StaffInvitation.ExpiresAtUtc)], cancellationToken: cancellationToken);
            code = HospitalRecordCodes.Format(canonical);
            expires = entity.ExpiresAtUtc;
            return WriteResult.Success;
        }, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
            ViewData["LifetimeDays"] = (int)options.Value.Lifetime.TotalDays;
            return View(input);
        }

        var registerUrl = Url.Page("/Account/Register", pageHandler: null, values: new { area = "Identity" }, protocol: Request.Scheme)!;
        var signupUrl = StaffInvitationCodes.SignupUrl(registerUrl, code!);
        var emailed = true;
        try
        {
            await email.SendEmailAsync(input.Email, "Your DRMC Patient Portal staff invitation",
                $"You have been invited to create a DRMC Patient Portal staff account ({HtmlEncoder.Default.Encode(StaffRoles.Label(input.Role))}). " +
                $"<a href='{HtmlEncoder.Default.Encode(signupUrl)}'>Create your account</a> using this email address. " +
                $"Your invitation code is {code}. It works once and expires on {Local(expires).ToString("MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)} (Philippine time). " +
                "Your staff role is added after you confirm your email and turn on two-factor authentication.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            emailed = false;
            logger.LogWarning(ex, "Staff invitation email could not be sent");
        }
        Response.Headers.CacheControl = "no-store";
        return View("Invited", new StaffInvitationSlip(input.Email, input.Role, code!, signupUrl, qr.GenerateSvgQrCode(signupUrl), Local(expires), emailed));
    }

    [HttpPost]
    public async Task<IActionResult> Revoke([FromRoute] int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var version = AdminAudit.Version(rowVersion);
        var result = version is null ? WriteResult.Invalid("Reload the staff invitations before continuing.") : await writes.RunAsync(async database =>
        {
            var invitation = await database.StaffInvitations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (invitation is null) return WriteResult.Invalid("That invitation no longer exists.");
            if (!version.SequenceEqual(invitation.RowVersion)) return WriteResult.Stale;
            if (invitation.StatusAt(DateTime.UtcNow) is not (StaffInvitationStatus.Invited or StaffInvitationStatus.AwaitingSetup))
                return WriteResult.Invalid("Only an unused invitation or one awaiting setup can be revoked.");
            database.Entry(invitation).Property(i => i.RowVersion).OriginalValue = version;
            invitation.RevokedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(database, User, "Revoke", Entity, id.ToString(CultureInfo.InvariantCulture),
                fields: [nameof(StaffInvitation.RevokedAtUtc)], cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) TempData["AdminStatus"] = "Invitation revoked. It can no longer be used, and no staff role will be granted from it.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private string ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("An Admin actor is required.");

    private static DateTime Local(DateTime utc) => ClinicalClock.WallTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
}
