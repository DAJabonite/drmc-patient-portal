using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class PhysicianNames
{
    public static async Task<string?> ResolveAsync(ApplicationDbContext db, IPhysicianChoice input, CancellationToken token)
    {
        if (input.PhysicianSource == PhysicianSource.HistoricalName)
            return string.IsNullOrWhiteSpace(input.HistoricalDoctorName) ? null : input.HistoricalDoctorName.Trim();
        if (input.PhysicianSource != PhysicianSource.Directory || input.DirectoryDoctorId is null) return null;
        var name = await db.Doctors.Where(d => d.Id == input.DirectoryDoctorId).Select(d => d.FullName).SingleOrDefaultAsync(token);
        return string.IsNullOrWhiteSpace(name) || name.Length > 100_000 ? null : name;
    }
    public static async Task<IReadOnlyList<SelectListItem>> OptionsAsync(ApplicationDbContext db, CancellationToken token) =>
        await db.Doctors.OrderBy(d => d.FullName).ThenBy(d => d.Id).Select(d => new SelectListItem(d.FullName + " | " + d.Department, d.Id.ToString())).ToListAsync(token);
}
