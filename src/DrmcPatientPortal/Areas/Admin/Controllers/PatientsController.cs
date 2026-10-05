using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PatientsController(ApplicationDbContext db, AdminWrites writes) :
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
    public Task<IActionResult> Unlink([FromRoute] int id, PatientLinkInput input, CancellationToken cancellationToken) =>
        ChangeLink(id, input, true, cancellationToken);

    private async Task<IActionResult> ChangeLink(int id, PatientLinkInput input, bool unlink, CancellationToken token)
    {
        var patient = await Find(Database, id, token);
        if (patient is null) return AdminConflicts.Missing(this);
        var version = AdminAudit.Version(input.RowVersion);
        if (version is null) ModelState.AddModelError(nameof(input.RowVersion), "Reload the patient record before continuing.");
        if (ModelState.IsValid)
        {
            var result = await Writes.RunAsync(async database =>
            {
                var current = await database.PatientRecords.SingleOrDefaultAsync(p => p.Id == id, token);
                if (current is null || !version!.SequenceEqual(current.RowVersion)) return WriteResult.Stale;
                var email = input.Email.Trim();
                var accounts = await database.Users.Where(u => u.Email != null &&
                    EF.Functions.Collate(u.Email, "Latin1_General_100_CI_AS") == email).Take(2).ToListAsync(token);
                if (accounts.Count != 1 || !accounts[0].EmailConfirmed)
                    return WriteResult.Invalid("Select one unambiguous existing email-confirmed account.");
                var account = accounts[0];
                if (unlink)
                {
                    if (current.PortalUserId != account.Id) return WriteResult.Invalid("The confirmed account is not linked to this patient.");
                }
                else
                {
                    if (current.PortalUserId is not null) return WriteResult.Invalid("Unlink the current account before creating a different link.");
                    if (await database.PatientRecords.AnyAsync(p => p.PortalUserId == account.Id, token))
                        return WriteResult.Invalid("That account is already linked to a patient record.");
                }
                database.Entry(current).Property(p => p.RowVersion).OriginalValue = version!;
                current.PortalUserId = unlink ? null : account.Id;
                await database.SaveChangesAsync(token);
                await AdminAudit.AddAsync(database, User, unlink ? "Unlink" : "Link", "Patients", id.ToString(), id,
                    fields: [nameof(PatientRecord.PortalUserId)], cancellationToken: token);
                return WriteResult.Success;
            }, token);
            if (result.Succeeded) return RedirectToAction(nameof(Details), new { area = "Admin", id });
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
        }
        return await LinkPage(patient, input, token);
    }

    private async Task<IActionResult> LinkPage(PatientRecord patient, PatientLinkInput input, CancellationToken token)
    {
        await AdminAudit.ViewAsync(Database, User, "Patients", [patient.Id], "Link", token);
        return View("Link", new PatientLinkPage(new(patient, "Verified portal link", patient.Id), input, patient.PortalUserId is not null));
    }
}
