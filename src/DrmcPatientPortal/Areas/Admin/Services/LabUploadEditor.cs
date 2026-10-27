using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

// Manual PDF uploads are independent of the structured lab import contract.
public sealed class LabUploadEditor : ClinicalEditor<LabResult, LabUploadInput>
{
    public override string Name => "LabResults";
    public override string Title => "Laboratory result";
    public override string SearchColumn => nameof(LabResult.TestName);
    public override int? PatientId(LabResult entity) => entity.PatientRecordId;
    public override string Summary(LabResult entity) => entity.AccessionNumber + " | " + entity.Status;
    public override IQueryable<LabResult> Search(IQueryable<LabResult> query, string search) => query.Where(l =>
        EF.Functions.Collate(l.AccessionNumber, "Latin1_General_100_CI_AS").Contains(search) ||
        EF.Functions.Collate(l.TestName, "Latin1_General_100_CI_AS").Contains(search));
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, LabResult entity) =>
        base.Fields(db, entity).Where(f => f.Label != nameof(LabResult.ReportFileName))
            .Select(f => f.Label == nameof(LabResult.AccessionNumber) ? f with { Label = "Report reference" } : f).ToArray();
    public override LabUploadInput Input(ApplicationDbContext db, LabResult entity) => new()
    {
        TestName = entity.TestName, Category = entity.Category, CollectedAt = entity.CollectedAt,
        ReleasedAt = entity.ReleasedAt, Status = entity.Status
    };
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, LabResult entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Lab items"] = await db.LabResultItems.CountAsync(i => i.LabResultId == entity.Id, token) };
    public override Task<WriteResult> ApplyAsync(ApplicationDbContext db, LabUploadInput input, LabResult entity,
        OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return Task.FromResult(WriteResult.Invalid("The patient record no longer exists."));
        if (create && input.Report is null) return Task.FromResult(WriteResult.Invalid("Choose the lab report PDF."));
        if (create)
        {
            entity.PatientRecordId = context.Patient.Id;
            entity.AccessionNumber = "PORTAL-LAB-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            entity.PerformingUnit = "";
        }
        entity.TestName = input.TestName.Trim(); entity.Category = input.Category; entity.Status = input.Status;
        entity.CollectedAt = WallTime(input.CollectedAt!.Value, create ? null : entity.CollectedAt);
        entity.ReleasedAt = input.ReleasedAt is null ? null : WallTime(input.ReleasedAt.Value, create ? null : entity.ReleasedAt);
        return Task.FromResult(WriteResult.Success);
    }
}
