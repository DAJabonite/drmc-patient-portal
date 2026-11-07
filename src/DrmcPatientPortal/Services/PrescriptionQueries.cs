using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class PrescriptionQueries
{
    public static async Task<List<RedStarEnrollment>> ValidEnrollmentsAsync(ApplicationDbContext db, CancellationToken token, int? hospitalId = null)
    {
        var result = new List<RedStarEnrollment>();
        var after = 0;
        while (true)
        {
            var batch = await db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient)
                .Where(e => e.Id > after && e.Program != RedStarProgram.Unverified && e.Patient.HospitalPatientRecordId != null &&
                    (!hospitalId.HasValue || e.Patient.HospitalPatientRecordId == hospitalId))
                .OrderBy(e => e.Id).Take(100).ToListAsync(token);
            if (batch.Count == 0) break;
            var ids = batch.Select(e => e.Patient.HospitalPatientRecordId!.Value).Distinct().ToArray();
            var hospitals = await db.PatientRecords.AsNoTracking().Where(p => EF.Parameter(ids).Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            result.AddRange(batch.Where(e => hospitals.TryGetValue(e.Patient.HospitalPatientRecordId!.Value, out var hospital) && RedStarReportAccess.MappingValid(e.Patient, hospital)));
            after = batch[^1].Id;
        }
        return result;
    }

    public static async Task<IQueryable<Prescription>> OwnedAsync(ApplicationDbContext db, string userId, CancellationToken token = default)
    {
        var owned = db.Prescriptions.AsNoTracking().Where(p => p.Patient.PortalUserId == userId &&
            (p.ClinicalEncounterId == null || p.Encounter!.PatientRecordId == p.PatientRecordId));
        var ids = await owned.Where(p => p.IsRedStar && p.RedStarEnrollmentId != null).Select(p => p.RedStarEnrollmentId!.Value).Distinct().ToArrayAsync(token);
        var valid = new List<int>();
        foreach (var chunk in ids.Chunk(100))
        {
            var enrollments = await db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).Where(e => EF.Parameter(chunk).Contains(e.Id)).ToListAsync(token);
            var hospitalIds = enrollments.Where(e => e.Patient.HospitalPatientRecordId.HasValue).Select(e => e.Patient.HospitalPatientRecordId!.Value).Distinct().ToArray();
            var hospitals = await db.PatientRecords.AsNoTracking().Where(p => EF.Parameter(hospitalIds).Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            valid.AddRange(enrollments.Where(e => e.Patient.PortalUserId == userId && e.Patient.HospitalPatientRecordId is int id &&
                hospitals.TryGetValue(id, out var hospital) && RedStarReportAccess.MappingValid(e.Patient, hospital)).Select(e => e.Id));
        }
        var validIds = valid.ToArray();
        return owned.Where(p => !p.IsRedStar || p.RedStarEnrollmentId.HasValue && EF.Parameter(validIds).Contains(p.RedStarEnrollmentId.Value)
            && p.RedStarEnrollment!.Patient.HospitalPatientRecordId == p.PatientRecordId && p.RedStarEnrollment.Program != RedStarProgram.Unverified)
            .Include(p => p.Encounter);
    }

    public static async Task<IQueryable<Prescription>> SearchAsync(IQueryable<Prescription> query, string search, CancellationToken token)
    {
        var matches = new List<int>();
        var after = 0;
        while (true)
        {
            var rows = await query.AsNoTracking().Where(p => p.Id > after).OrderBy(p => p.Id).Take(100)
                .Select(p => new { p.Id, p.RxNumber, p.GenericName }).ToListAsync(token);
            if (rows.Count == 0) break;
            matches.AddRange(rows.Where(p => RedStarPhiProtection.Contains(p.RxNumber, search) || RedStarPhiProtection.Contains(p.GenericName, search)).Select(p => p.Id));
            after = rows[^1].Id;
        }
        var ids = matches.ToArray();
        return query.Where(p => EF.Parameter(ids).Contains(p.Id));
    }
}
