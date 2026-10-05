using System.Globalization;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class DoctorsController(ApplicationDbContext db, AdminWrites writes) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 450) { ModelState.AddModelError(nameof(search), "Search must be at most 450 characters."); search = null; }
        var query = db.Doctors.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(d =>
            EF.Functions.Collate(d.FullName, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(d.Department, "Latin1_General_100_CI_AS").Contains(search));
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        return View(new DoctorList(await query.OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken), search, page, total));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var doctor = await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return doctor is null ? NotFound() : View(doctor);
    }

    [HttpGet]
    public IActionResult Create() => View(new DoctorInput());

    [HttpPost]
    public async Task<IActionResult> Create(DoctorInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        var result = await writes.RunAsync(async context =>
        {
            var doctor = new Doctor(); input.Apply(doctor); context.Doctors.Add(doctor);
            await context.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(context, User, "Create", "Doctors", doctor.Id.ToString(CultureInfo.InvariantCulture),
                after: AdminAudit.Values(context, doctor), cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return Result(result, input, "Create");
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doctor is null) return NotFound();
        ViewData["Id"] = id;
        return View(DoctorInput.From(doctor, (byte[])db.Entry(doctor).Property("RowVersion").CurrentValue!));
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, DoctorInput input, CancellationToken cancellationToken)
    {
        ViewData["Id"] = id;
        var version = AdminAudit.Version(input.RowVersion);
        if (version is null) ModelState.AddModelError(nameof(input.RowVersion), "Reload the record to obtain its current version.");
        if (!ModelState.IsValid) return View(input);
        var result = await writes.RunAsync(async context =>
        {
            var doctor = await context.Doctors.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (doctor is null) return WriteResult.Stale;
            if (!version!.SequenceEqual((byte[])context.Entry(doctor).Property("RowVersion").CurrentValue!)) return WriteResult.Stale;
            context.Entry(doctor).Property("RowVersion").OriginalValue = version;
            var before = AdminAudit.Values(context, doctor); input.Apply(doctor);
            await context.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(context, User, "Edit", "Doctors", id.ToString(CultureInfo.InvariantCulture),
                before: before, after: AdminAudit.Values(context, doctor), cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return Result(result, input, "Edit");
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doctor is null) return NotFound();
        ViewData["RowVersion"] = Convert.ToBase64String((byte[])db.Entry(doctor).Property("RowVersion").CurrentValue!);
        return View(doctor);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] int id, string? rowVersion, CancellationToken cancellationToken)
    {
        var version = AdminAudit.Version(rowVersion);
        var result = version is null ? WriteResult.Stale : await writes.RunAsync(async context =>
        {
            var doctor = await context.Doctors.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (doctor is null) return WriteResult.Stale;
            context.Entry(doctor).Property("RowVersion").OriginalValue = version;
            var before = AdminAudit.Values(context, doctor); context.Doctors.Remove(doctor);
            await context.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(context, User, "Delete", "Doctors", id.ToString(CultureInfo.InvariantCulture),
                before: before, cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { area = "Admin" });
        if (result.Conflict && !await db.Doctors.AnyAsync(d => d.Id == id, cancellationToken))
            return AdminConflicts.Missing(this);
        TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Delete), new { area = "Admin", id });
    }

    private IActionResult Result(WriteResult result, DoctorInput input, string view)
    {
        if (result.Succeeded) return RedirectToAction(nameof(Index), new { area = "Admin" });
        if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError("", result.Error!);
        return View(view, input);
    }
}
