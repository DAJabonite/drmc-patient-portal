using System.Globalization;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

// Issues single-use hospital record codes at the PACD or a clinic desk after staff check the
// patient's identity in person. Patient services staff see only the patient name and hospital
// number in lists; birth date appears on the issue form so it can be checked against the ID.
[Area("Admin")]
public sealed class RegistrationCodesController(ApplicationDbContext db, AdminWrites writes, IQrCodeService qr,
    IOptions<PatientRegistrationOptions> options) : Controller
{
    private const string Entity = "RegistrationCodes";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = Search(search);
        var query = db.PatientRegistrationCodes.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(c =>
            EF.Functions.Collate(c.PatientRecord.FullName, "Latin1_General_100_CI_AS").Contains(search) ||
            c.PatientRecord.HospitalNumber != null && EF.Functions.Collate(c.PatientRecord.HospitalNumber, "Latin1_General_100_CI_AS").Contains(search) ||
            c.CodeHint == search.ToUpper());
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var codes = await query.OrderByDescending(c => c.Id).Skip((page - 1) * 25).Take(25)
            .Select(c => new { Code = c, c.PatientRecord.FullName, c.PatientRecord.HospitalNumber }).ToListAsync(cancellationToken);
        await AdminAudit.ViewAsync(db, User, Entity, codes.Select(c => c.Code.PatientRecordId).Distinct(), "Index", cancellationToken);
        var now = DateTime.UtcNow;
        var rows = codes.Select(c => new RegistrationCodeRow(c.Code.Id, c.Code.PatientRecordId, c.FullName, c.HospitalNumber, c.Code.CodeHint,
            c.Code.IssuingPoint, Local(c.Code.IssuedAtUtc), Local(c.Code.ExpiresAtUtc), c.Code.RedeemedAtUtc is { } r ? Local(r) : null,
            c.Code.StatusAt(now), Convert.ToBase64String(c.Code.RowVersion))).ToArray();
        return View(new RegistrationCodeList(rows, search, page, total));
    }

    [HttpGet]
    public async Task<IActionResult> Create([FromRoute] int id = 0, string? search = null, int page = 1, CancellationToken cancellationToken = default)
    {
        if (id == 0) return await SelectPatient(search, page, cancellationToken);
        var patient = await db.PatientRecords.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (patient is null) return NotFound();
        return await IssuePage(patient, new RegistrationCodeInput(), cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromRoute] int id, RegistrationCodeInput input, CancellationToken cancellationToken)
    {
        var patient = await db.PatientRecords.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (patient is null) return NotFound();
        if (!RegistrationCodeIssuingPoints.All.Contains(input.IssuingPoint))
            ModelState.AddModelError("Input." + nameof(input.IssuingPoint), "Select where the code is being issued.");
        if (!input.IdentityVerified) ModelState.AddModelError("Input." + nameof(input.IdentityVerified), RegistrationCodeInput.IdentityError);
        if (!ModelState.IsValid) return await IssuePage(patient, input, cancellationToken);

        string? code = null;
        DateTime expires = default;
        var result = await writes.RunAsync(async database =>
        {
            var current = await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (current is null) return WriteResult.Invalid("The patient record no longer exists.");
            if (current.PortalUserId is not null) return WriteResult.Invalid("This hospital record is already linked to a portal account, so it does not need a code.");
            if (current.DateOfBirth is null)
                return WriteResult.Invalid("Add the patient's birth date to the hospital record first. Signup checks the code against it.");
            var now = DateTime.UtcNow;
            // One active code per record: issuing a new one replaces any earlier code.
            var active = await database.PatientRegistrationCodes.Where(c => c.PatientRecordId == id && c.RedeemedAtUtc == null &&
                c.RevokedAtUtc == null && c.ExpiresAtUtc > now).ToListAsync(cancellationToken);
            foreach (var earlier in active) earlier.RevokedAtUtc = now;
            var canonical = HospitalRecordCodes.Normalize(HospitalRecordCodes.Generate())!;
            var issuedBy = await database.Users.AsNoTracking().Where(u => u.Id == ActorId).Select(u => new { u.Id, u.Email }).SingleAsync(cancellationToken);
            var entity = new PatientRegistrationCode
            {
                PatientRecordId = id, CodeHash = HospitalRecordCodes.Hash(canonical), CodeHint = HospitalRecordCodes.Hint(canonical),
                IssuingPoint = input.IssuingPoint, IssuedById = issuedBy.Id, IssuedByEmail = issuedBy.Email ?? string.Empty,
                IssuedAtUtc = now, ExpiresAtUtc = now + options.Value.CodeLifetime,
            };
            database.PatientRegistrationCodes.Add(entity);
            await database.SaveChangesAsync(cancellationToken);
            foreach (var earlier in active)
                await AdminAudit.AddAsync(database, User, "Revoke", Entity, earlier.Id.ToString(CultureInfo.InvariantCulture), id,
                    fields: [nameof(PatientRegistrationCode.RevokedAtUtc)], cancellationToken: cancellationToken);
            await AdminAudit.AddAsync(database, User, "Issue", Entity, entity.Id.ToString(CultureInfo.InvariantCulture), id,
                fields: [nameof(PatientRegistrationCode.IssuingPoint), nameof(PatientRegistrationCode.ExpiresAtUtc)], cancellationToken: cancellationToken);
            code = HospitalRecordCodes.Format(canonical);
            expires = entity.ExpiresAtUtc;
            return WriteResult.Success;
        }, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
            return await IssuePage(patient, input, cancellationToken);
        }

        Response.Headers.CacheControl = "no-store";
        var registerUrl = Url.Page("/Account/Register", pageHandler: null, values: new { area = "Identity" }, protocol: Request.Scheme)!;
        var signupUrl = HospitalRecordCodes.SignupUrl(registerUrl, code!);
        return View("Issued", new RegistrationCodeSlip(new(patient, "Hospital record code", patient.Id), code!, signupUrl,
            qr.GenerateSvgQrCode(signupUrl), input.IssuingPoint, Local(expires)));
    }

    [HttpPost]
    public async Task<IActionResult> Revoke([FromRoute] int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var version = AdminAudit.Version(rowVersion);
        var result = version is null ? WriteResult.Invalid("Reload the registration codes before continuing.") : await writes.RunAsync(async database =>
        {
            var code = await database.PatientRegistrationCodes.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (code is null) return WriteResult.Invalid("That registration code no longer exists.");
            if (!version.SequenceEqual(code.RowVersion)) return WriteResult.Stale;
            if (code.StatusAt(DateTime.UtcNow) != RegistrationCodeStatus.Active) return WriteResult.Invalid("Only an active code can be revoked.");
            database.Entry(code).Property(c => c.RowVersion).OriginalValue = version;
            code.RevokedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(database, User, "Revoke", Entity, id.ToString(CultureInfo.InvariantCulture), code.PatientRecordId,
                fields: [nameof(PatientRegistrationCode.RevokedAtUtc)], cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) TempData["AdminStatus"] = "Registration code revoked. It can no longer be used to sign up.";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task<IActionResult> SelectPatient(string? search, int page, CancellationToken token)
    {
        search = Search(search);
        var query = db.PatientRecords.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(p => EF.Functions.Collate(p.FullName, "Latin1_General_100_CI_AS").Contains(search) ||
            p.HospitalNumber != null && EF.Functions.Collate(p.HospitalNumber, "Latin1_General_100_CI_AS").Contains(search));
        var total = await query.CountAsync(token);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var patients = await query.OrderBy(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        await AdminAudit.ViewAsync(db, User, Entity, patients.Select(p => p.Id), "Create context", token);
        ViewData["ContextAction"] = "Create";
        return View("/Areas/Admin/Views/Shared/SelectContext.cshtml", new AdminList("Select patient", patients.Select(p => new AdminRow(p.Id, p.FullName,
            "Hospital number: " + (p.HospitalNumber ?? "unassigned") + " | " + (p.PortalUserId is null ? "Unlinked" : "Linked"), p.Id)).ToArray(), search, page, total));
    }

    private async Task<IActionResult> IssuePage(PatientRecord patient, RegistrationCodeInput input, CancellationToken token)
    {
        await AdminAudit.ViewAsync(db, User, Entity, [patient.Id], "Create", token);
        var now = DateTime.UtcNow;
        var active = await db.PatientRegistrationCodes.CountAsync(c => c.PatientRecordId == patient.Id && c.RedeemedAtUtc == null &&
            c.RevokedAtUtc == null && c.ExpiresAtUtc > now, token);
        ViewData["LifetimeDays"] = (int)options.Value.CodeLifetime.TotalDays;
        return View("Create", new RegistrationCodeIssuePage(new(patient, "Hospital record code", patient.Id), input,
            patient.PortalUserId is not null, patient.DateOfBirth is not null, active));
    }

    private string? Search(string? search)
    {
        search = search?.Trim();
        if (search?.Length > 450) { ModelState.AddModelError(nameof(search), "Search must be at most 450 characters."); return null; }
        return search;
    }

    private string ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("A staff actor is required.");

    private static DateTime Local(DateTime utc) => ClinicalClock.WallTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
}
