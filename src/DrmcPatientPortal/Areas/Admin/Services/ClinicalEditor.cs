using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public abstract class ClinicalEditor<TEntity, TInput> : AdminEntity<TEntity, TInput>
    where TEntity : class, new() where TInput : class, IAdminInput, new()
{
    public override bool Clinical => true;
    public override bool NeedsContext => true;
    public override IQueryable<TEntity> Query(ApplicationDbContext db, int? contextId = null) => contextId is null ? db.Set<TEntity>() :
        db.Set<TEntity>().Where(e => EF.Property<int>(e, "PatientRecordId") == contextId);
    public override async Task<OwnershipContext?> CreateContextAsync(ApplicationDbContext db, int routeId, CancellationToken token)
    {
        var patient = await SqlReadRetry.RunAsync(db, () => db.PatientRecords.SingleOrDefaultAsync(p => p.Id == routeId, token), token);
        return patient is null ? null : new(patient, Title, patient.Id);
    }
    public override Task<OwnershipContext?> RecordContextAsync(ApplicationDbContext db, TEntity entity, CancellationToken token) =>
        CreateContextAsync(db, PatientId(entity)!.Value, token);
    public override async Task<AdminList> SelectContextAsync(ApplicationDbContext db, string? search, int page, CancellationToken token)
    {
        var query = db.PatientRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => EF.Functions.Collate(p.FullName, "Latin1_General_100_CI_AS").Contains(search) ||
            p.HospitalNumber != null && EF.Functions.Collate(p.HospitalNumber, "Latin1_General_100_CI_AS").Contains(search));
        var total = await SqlReadRetry.RunAsync(db, () => query.CountAsync(token), token); page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var patients = await SqlReadRetry.RunAsync(db, () => query.OrderBy(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token), token);
        return new("Select patient", patients.Select(p => new AdminRow(p.Id, p.FullName, "Hospital number: " + (p.HospitalNumber ?? "unassigned"), p.Id)).ToArray(), search, page, total);
    }
}
