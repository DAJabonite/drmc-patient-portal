using DrmcPatientPortal.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class EncounterLinks
{
    public static async Task<IReadOnlyList<SelectListItem>> OptionsAsync(ApplicationDbContext db, int patientId, CancellationToken token) =>
        (await db.ClinicalEncounters.AsNoTracking().Where(e => e.PatientRecordId == patientId)
            .OrderByDescending(e => e.EncounterDate).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.EncounterReference, e.EncounterDate, e.Department }).ToListAsync(token))
        .Select(e => new SelectListItem($"{e.EncounterReference} · {e.EncounterDate:yyyy-MM-dd} · {e.Department}", e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();

    public static async Task<bool> ValidAsync(ApplicationDbContext db, int? encounterId, int patientId,
        bool required, CancellationToken token, ImportLookups? lookups = null)
    {
        if (encounterId is null) return !required;
        if (lookups is not null) return lookups.Encounters.TryGetValue(encounterId.Value, out var parent) && parent.PatientRecordId == patientId;
        return db.ClinicalEncounters.Local.Any(e => db.Entry(e).State == EntityState.Added && e.Id == encounterId && e.PatientRecordId == patientId)
            || await db.ClinicalEncounters.AnyAsync(e => e.Id == encounterId && e.PatientRecordId == patientId, token);
    }
}
