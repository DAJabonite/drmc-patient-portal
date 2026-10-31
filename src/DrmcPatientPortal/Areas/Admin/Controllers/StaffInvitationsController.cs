using System.Globalization;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Localization;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

// Admin-only (not in the staff policy registry). Invites a DRMC employee to create a staff account
// without a hospital record code. The invitation is bound to one email and one grantable role;
// it never grants Admin. The role is added after the new account confirms its email and enables
// two-factor authentication.
[Area("Admin")]
public sealed class StaffInvitationsController(ApplicationDbContext db, AdminWrites writes, IQrCodeService qr, IOutboundNotificationQueue queue,
    ILookupNormalizer normalizer, IOptions<StaffInvitationOptions> options, AccountLinks links, TimeProvider clock,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    private const string Entity = "StaffInvitations";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 256) { ModelState.AddModelError(nameof(search), "Search must be at most 256 characters."); search = null; }
        var query = db.StaffInvitations.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(i => EF.Functions.Collate(i.Email, "Latin1_General_100_CI_AS").Contains(search) || i.CodeHint == search.ToUpper());
        var total = await SqlReadRetry.RunAsync(db, () => query.CountAsync(cancellationToken), cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var invitations = await SqlReadRetry.RunAsync(db, () => query.OrderByDescending(i => i.Id).Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken), cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
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
        string? signupUrl = null;
        DateTime expires = default;
        var registerUrl = links.Page(HttpContext, "/Account/Register", new { area = "Identity" });
        var messageId = Guid.NewGuid();
        var result = await writes.RunAsync(async database =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
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
            await queue.CancelInvitationsAsync(database, open.Select(i => i.Id).ToArray(), cancellationToken);
            code = HospitalRecordCodes.Format(canonical);
            expires = entity.ExpiresAtUtc;
            signupUrl = StaffInvitationCodes.SignupUrl(registerUrl, code);
            queue.Stage(database, messageId, OutboundNotificationChannel.Email,
                InvitationEmail(input.Email, input.Role, code, signupUrl, expires),
                new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)), entity.Id);
            foreach (var earlier in open)
                await AdminAudit.AddAsync(database, User, "Revoke", Entity, earlier.Id.ToString(CultureInfo.InvariantCulture),
                    fields: [nameof(StaffInvitation.RevokedAtUtc)], cancellationToken: cancellationToken);
            await AdminAudit.AddAsync(database, User, "Invite", Entity, entity.Id.ToString(CultureInfo.InvariantCulture),
                fields: [nameof(StaffInvitation.Email), nameof(StaffInvitation.Role), nameof(StaffInvitation.ExpiresAtUtc)], cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
            ViewData["LifetimeDays"] = (int)options.Value.Lifetime.TotalDays;
            return View(input);
        }

        Response.Headers.CacheControl = "no-store";
        return View("Invited", new StaffInvitationSlip(input.Email, input.Role, code!, signupUrl!, qr.GenerateSvgQrCode(signupUrl!), Local(expires), EmailQueued: true));
    }

    private OutboundNotificationPayload InvitationEmail(string email, string role, string code, string signupUrl, DateTime expires)
    {
        // Render now: the background worker's culture is not the issuing request's culture.
        var intro = localizer["StaffInvite_EmailIntro"].Value;
        var assignedRole = localizer["StaffInvite_EmailRole", StaffRoles.Label(role)].Value;
        var address = localizer["StaffInvite_EmailAddress"].Value;
        var linkText = localizer["StaffInvite_EmailLink"].Value;
        var invitationCode = localizer["StaffInvite_EmailCode", code].Value;
        var expiry = localizer["StaffInvite_EmailExpiry", Local(expires).ToString("MMMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)].Value;
        var setup = localizer["StaffInvite_EmailSetup"].Value;
        var content = PortalEmailRenderer.Render(new PortalEmailContent(
            Subject: localizer["StaffInvite_EmailSubject"].Value,
            Heading: linkText,
            Preview: localizer["StaffInvite_EmailPreview"].Value,
            Introduction: intro,
            ActionText: linkText,
            ActionUrl: signupUrl,
            Instructions: $"{address} {setup}",
            UnexpectedGuidance: localizer["PortalEmail_InvitationUnexpected"].Value,
            Details: [new(assignedRole), new(invitationCode, IsCode: true), new(expiry)],
            Language: CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            FallbackLabel: localizer["PortalEmail_Fallback"].Value));
        return new(email, content.Subject, content.HtmlBody, content.PlainTextBody);
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
            if (invitation.StatusAt(clock.GetUtcNow().UtcDateTime) is not (StaffInvitationStatus.Invited or StaffInvitationStatus.AwaitingSetup))
                return WriteResult.Invalid("Only an unused invitation or one awaiting setup can be revoked.");
            database.Entry(invitation).Property(i => i.RowVersion).OriginalValue = version;
            invitation.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
            await database.SaveChangesAsync(cancellationToken);
            await queue.CancelInvitationsAsync(database, [id], cancellationToken);
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
