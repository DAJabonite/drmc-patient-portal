using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class AuditController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int? patientId, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 450) { ModelState.AddModelError(nameof(search), "Search must be at most 450 characters."); search = null; }
        var query = db.AdminAuditLogs.AsNoTracking().AsQueryable();
        if (patientId.HasValue) query = query.Where(a => a.SubjectPatientId == patientId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(a =>
            EF.Functions.Collate(a.ActorEmail, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(a.Entity, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(a.Action, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(a.RecordKey, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(a.ChangedFields, "Latin1_General_100_CI_AS").Contains(search));
        var count = await query.CountAsync(cancellationToken); page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
        var rows = await query.OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id).Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken);
        await AdminAudit.ViewAsync(db, User, "Audit", rows.Where(a => a.SubjectPatientId.HasValue).Select(a => a.SubjectPatientId!.Value), "Index", cancellationToken);
        return View(new AuditPage(rows, search, patientId, page, count));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] long id, CancellationToken cancellationToken)
    {
        var row = await db.AdminAuditLogs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (row is null) return NotFound();
        if (row.SubjectPatientId.HasValue) await AdminAudit.ViewAsync(db, User, "Audit", [row.SubjectPatientId.Value], id.ToString(), cancellationToken);
        return View(row);
    }
}
