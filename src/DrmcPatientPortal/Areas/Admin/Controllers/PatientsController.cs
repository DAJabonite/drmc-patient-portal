using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PatientsController(ApplicationDbContext db, AdminWrites writes, IAuditLogService audit) :
    AdminCrudController<PatientRecord, PatientInput>(db, writes, new PatientEditor())
{
    [HttpGet]
    public async Task<IActionResult> Link([FromRoute] int id, CancellationToken cancellationToken)
    {
        var patient = await Find(Database, id, cancellationToken);
        if (patient is null) return NotFound();
        return await LinkPage(patient, new PatientLinkInput { RowVersion = Convert.ToBase64String(patient.RowVersion) }, cancellationToken);
    }

    [HttpPost]
    public Task<IActionResult> Link([FromRoute] int id, PatientLinkInput input, CancellationToken cancellationToken) =>
        ChangeLink(id, input, false, cancellationToken);

    [HttpPost]
    public Task<IActionResult> Unlink([FromRoute] int id, PatientUnlinkInput input, CancellationToken cancellationToken) =>
        ChangeLink(id, input, true, cancellationToken);

    private async Task<IActionResult> ChangeLink(int id, PatientAccountInput input, bool unlink, CancellationToken token)
    {
        var patient = await Find(Database, id, token);
        if (patient is null)
        {
            await HospitalRecordLinkAudit.RejectedAsync(audit, HttpContext, "staff_record_missing", $"PatientRecord/{id}");
            return AdminConflicts.Missing(this);
        }
        var version = AdminAudit.Version(input.RowVersion);
        if (version is null) ModelState.AddModelError(nameof(input.RowVersion), "Reload the patient record before continuing.");
        int? mismatchCodeId = null;
        if (ModelState.IsValid)
        {
            var result = await Writes.RunAsync(async database =>
            {
                // RunAsync can retry a deadlock; only the final attempt may record a mismatch.
                mismatchCodeId = null;
                var current = await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == id, token);
                if (current is null || !version!.SequenceEqual(current.RowVersion)) return WriteResult.Stale;
                PatientRegistrationCode? recordCode = null;
                if (!unlink)
                {
                    var canonical = HospitalRecordCodes.Normalize(((PatientLinkInput)input).HospitalRecordCode);
                    if (canonical is null) return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                    var hash = HospitalRecordCodes.Hash(canonical);
                    recordCode = await database.PatientRegistrationCodes.AsNoTracking().SingleOrDefaultAsync(c => c.CodeHash == hash, token);
                    if (recordCode is null || recordCode.StatusAt(DateTime.UtcNow) != RegistrationCodeStatus.Active)
                        return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                }
                var email = input.Email.Trim();
                var accounts = await database.Users.Where(u => u.Email != null &&
                    EF.Functions.Collate(u.Email, "Latin1_General_100_CI_AS") == email).Take(2).ToListAsync(token);
                if (accounts.Count != 1 || !accounts[0].EmailConfirmed)
                {
                    mismatchCodeId = recordCode?.Id;
                    return WriteResult.Invalid(unlink ? "Select one unambiguous existing email-confirmed account." : HospitalRecordCodes.LinkError);
                }
                var account = accounts[0];
                if (unlink)
                {
                    if (current.PortalUserId != account.Id) return WriteResult.Invalid("The confirmed account is not linked to this patient.");
                }
                else
                {
                    var proof = (PatientLinkInput)input;
                    if (recordCode!.PatientRecordId != current.Id || !HospitalRecordCodes.MatchesRecord(current, proof.HospitalNumber,
                            account.LastName, account.FirstName, account.DateOfBirth, account.MiddleName, account.Suffix))
                    {
                        mismatchCodeId = recordCode.Id;
                        return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                    }
                    if (current.PortalUserId is not null) return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                    if (await database.PatientRecords.AnyAsync(p => p.PortalUserId == account.Id, token))
                        return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                    var redeemedAt = DateTime.UtcNow;
                    var redeemed = await database.PatientRegistrationCodes.Where(c => c.Id == recordCode.Id && c.RowVersion == recordCode.RowVersion
                            && c.RedeemedAtUtc == null && c.RevokedAtUtc == null && c.LockedAtUtc == null
                            && c.FailedAttemptCount < PatientRegistrationCode.MaximumFailedAttempts && c.ExpiresAtUtc > redeemedAt)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedeemedAtUtc, redeemedAt).SetProperty(c => c.RedeemedByUserId, account.Id), token);
                    if (redeemed != 1) return WriteResult.Invalid(HospitalRecordCodes.LinkError);
                }
                database.Entry(current).Property(p => p.RowVersion).OriginalValue = version!;
                current.PortalUserId = unlink ? null : account.Id;
                await database.SaveChangesAsync(token);
                // Previously issued codes must never become usable again after a staff unlink.
                var now = DateTime.UtcNow;
                await database.PatientRegistrationCodes.Where(c => c.PatientRecordId == id && c.RedeemedAtUtc == null && c.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAtUtc, now), token);
                await AdminAudit.AddAsync(database, User, unlink ? "Unlink" : "Link", "Patients", id.ToString(), id,
                    fields: [nameof(PatientRecord.PortalUserId)], cancellationToken: token);
                return WriteResult.Success;
            }, token);
            if (result.Succeeded) return RedirectToAction(nameof(Details), new { area = "Admin", id });
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
        }
        else if (input is PatientLinkInput proof && version is not null && version.SequenceEqual(patient.RowVersion)
            && ModelState.TryGetValue(nameof(PatientLinkInput.HospitalNumber), out var hospitalNumberState) && hospitalNumberState.Errors.Count > 0)
        {
            // Required/length validation must not bypass the attempt limit for a known code.
            // Unrelated form errors and stale or missing rowversions do not consume attempts.
            var canonical = HospitalRecordCodes.Normalize(proof.HospitalRecordCode);
            if (canonical is not null)
            {
                var hash = HospitalRecordCodes.Hash(canonical);
                var recordCode = await Database.PatientRegistrationCodes.AsNoTracking().SingleOrDefaultAsync(c => c.CodeHash == hash, token);
                if (recordCode?.StatusAt(DateTime.UtcNow) == RegistrationCodeStatus.Active) mismatchCodeId = recordCode.Id;
            }
        }
        if (mismatchCodeId is int codeId)
            await HospitalRecordCodes.RejectIdentityMismatchAsync(Database, audit, HttpContext, codeId, "staff_link_rejected", $"PatientRecord/{id}");
        else
            await HospitalRecordLinkAudit.RejectedAsync(audit, HttpContext, unlink ? "staff_unlink_rejected" : "staff_link_rejected", $"PatientRecord/{id}");
        return await LinkPage(patient, input, token);
    }

    private async Task<IActionResult> LinkPage(PatientRecord patient, PatientAccountInput input, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        await AdminAudit.ViewAsync(Database, User, "Patients", [patient.Id], "Link", token);
        var linkInput = input as PatientLinkInput ?? new PatientLinkInput
            { Email = input.Email, IdentityVerified = input.IdentityVerified, RowVersion = input.RowVersion };
        var unlinkInput = new PatientUnlinkInput
            { Email = input.Email, IdentityVerified = input.IdentityVerified, RowVersion = input.RowVersion };
        return View("Link", new PatientLinkPage(new(patient, "Verified portal link", patient.Id), linkInput, unlinkInput, patient.PortalUserId is not null));
    }
}
