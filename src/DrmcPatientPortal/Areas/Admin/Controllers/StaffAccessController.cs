using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

// Admin-only (not in the staff policy registry). Grants or revokes only LabStaff and RadiologyStaff;
// it never grants Admin and never edits other Identity data.
[Area("Admin")]
public sealed class StaffAccessController(ApplicationDbContext db, AdminWrites writes) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool all = false, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 256) { ModelState.AddModelError(nameof(search), "Search must be at most 256 characters."); search = null; }
        var staffRoleIds = db.Roles.Where(r => StaffRoles.All.Contains(r.Name!)).Select(r => r.Id);
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!all) query = query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && staffRoleIds.Contains(ur.RoleId)));
        if (!string.IsNullOrEmpty(search)) query = query.Where(u =>
            u.Email != null && EF.Functions.Collate(u.Email, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(u.FullName, "Latin1_General_100_CI_AS").Contains(search));
        var total = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var now = DateTimeOffset.UtcNow;
        var users = await query.OrderBy(u => u.Email).ThenBy(u => u.Id).Skip((page - 1) * 25).Take(25)
            .Select(u => new { u.Id, u.Email, u.FullName, u.EmailConfirmed, u.TwoFactorEnabled, u.LockoutEnd, u.ConcurrencyStamp }).ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToArray();
        var memberships = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                                 where ids.Contains(ur.UserId) && StaffRoles.All.Contains(r.Name!) select new { ur.UserId, r.Name }).ToListAsync(cancellationToken);
        var rows = users.Select(u => new StaffAccountRow(u.Id, u.Email ?? "", u.FullName, u.EmailConfirmed, u.TwoFactorEnabled, u.LockoutEnd > now,
            memberships.Where(m => m.UserId == u.Id).Select(m => m.Name!).OrderBy(n => n).ToArray(), u.ConcurrencyStamp ?? "")).ToArray();
        return View(new StaffAccessPage(rows, search, all, page, total));
    }

    [HttpPost]
    public async Task<IActionResult> Update(string userId, string role, bool grant, string? concurrencyStamp, string? search, bool all = false, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!StaffRoles.Grantable.Contains(role)) return BadRequest();
        var result = await writes.RunAsync(async database =>
        {
            var user = await database.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null) return WriteResult.Invalid("That account no longer exists.");
            if (string.IsNullOrEmpty(concurrencyStamp) || user.ConcurrencyStamp != concurrencyStamp) return WriteResult.Stale;
            var roleId = await database.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync(cancellationToken);
            var membership = await database.UserRoles.SingleOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, cancellationToken);
            if (grant)
            {
                if (!user.EmailConfirmed || !user.TwoFactorEnabled) return WriteResult.Invalid("Staff roles need an email-confirmed account with two-factor authentication enabled.");
                if (membership is not null) return WriteResult.Invalid("That account already has this role.");
                database.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
            }
            else
            {
                if (membership is null) return WriteResult.Invalid("That account does not have this role.");
                database.UserRoles.Remove(membership);
            }
            // A new security stamp refreshes or ends existing sessions; the new concurrency stamp
            // rejects any other grant or revoke prepared against the earlier account state.
            user.SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
            database.Entry(user).Property(u => u.ConcurrencyStamp).OriginalValue = concurrencyStamp;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            await database.SaveChangesAsync(cancellationToken);
            await AdminAudit.AddAsync(database, User, grant ? "Grant" : "Revoke", "StaffAccess", userId, fields: [role], cancellationToken: cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) TempData["AdminSuccess"] = (grant ? "Granted " : "Revoked ") + StaffRoles.Label(role) + ".";
        else TempData["AdminError"] = result.Error;
        return RedirectToAction(nameof(Index), new { area = "Admin", search, all, page });
    }
}
