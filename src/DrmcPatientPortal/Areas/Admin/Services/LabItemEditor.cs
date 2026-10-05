using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class LabItemEditor : AdminEntity<LabResultItem, LabItemInput>
{
    public override string Name => "LabResultItems";
    public override string Title => "Lab items";
    public override string SearchColumn => "ParameterName";
    public override bool Clinical => true;
    public override bool NeedsContext => true;
    public override bool RequiresListContext => true;
    public override IQueryable<LabResultItem> Query(ApplicationDbContext db, int? contextId = null)
    {
        var query = db.LabResultItems.Include(i => i.LabResult).AsQueryable();
        return contextId is null ? query : query.Where(i => i.LabResultId == contextId);
    }
    public override int? PatientId(LabResultItem entity) => entity.LabResult.PatientRecordId;
    public override string Summary(LabResultItem entity) => entity.Unit + " | " + entity.Flag;
    public override async Task<OwnershipContext?> CreateContextAsync(ApplicationDbContext db, int routeId, CancellationToken token)
    {
        var lab = await db.LabResults.Include(l => l.Patient).SingleOrDefaultAsync(l => l.Id == routeId, token);
        return lab is null ? null : new(lab.Patient, "Lab: " + lab.AccessionNumber + " | " + lab.TestName, lab.Id);
    }
    public override Task<OwnershipContext?> RecordContextAsync(ApplicationDbContext db, LabResultItem entity, CancellationToken token) => CreateContextAsync(db, entity.LabResultId, token);
    public override async Task<AdminList> SelectContextAsync(ApplicationDbContext db, string? search, int page, CancellationToken token)
    {
        var query = db.LabResults.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(l => EF.Functions.Collate(l.AccessionNumber, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(l.TestName, "Latin1_General_100_CI_AS").Contains(search));
        var count = await query.CountAsync(token); page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
        var labs = await query.OrderByDescending(l => l.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        return new("Select laboratory", labs.Select(l => new AdminRow(l.Id, l.AccessionNumber, l.TestName + " | Patient #" + l.PatientRecordId, l.PatientRecordId)).ToArray(), search, page, count);
    }
    public override LabItemInput Input(ApplicationDbContext db, LabResultItem entity) => new()
    { ParameterName = entity.ParameterName, Value = entity.Value, Unit = entity.Unit, ReferenceRange = entity.ReferenceRange, Flag = entity.Flag };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, LabItemInput input, LabResultItem entity, OwnershipContext? context, bool create, CancellationToken token)
    {
        if (context is null) return WriteResult.Invalid("The parent lab no longer exists.");
        var parent = await db.LabResults.SingleOrDefaultAsync(l => l.Id == context.RouteId && l.PatientRecordId == context.Patient.Id, token);
        if (parent is null) return WriteResult.Invalid("The lab does not belong to the selected patient.");
        if (create) { entity.LabResultId = parent.Id; entity.LabResult = parent; }
        else if (entity.LabResultId != parent.Id) return WriteResult.Invalid("Lab item ownership cannot change.");
        entity.ParameterName = input.ParameterName.Trim(); entity.Value = input.Value ?? ""; entity.Unit = input.Unit ?? "";
        entity.ReferenceRange = input.ReferenceRange ?? ""; entity.Flag = input.Flag; return WriteResult.Success;
    }
}
