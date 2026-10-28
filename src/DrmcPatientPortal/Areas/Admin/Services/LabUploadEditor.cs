using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

// Manual PDF uploads are independent of the structured lab import contract.
public sealed class LabUploadEditor : ClinicalEditor<LabResult, LabUploadInput>
{
    internal const string MissingHospitalNumberError = "Add a hospital number to the patient record before uploading a report.";
    public override string Name => "LabResults";
    public override string Title => "Laboratory result";
    public override string SearchColumn => nameof(LabResult.TestName);
    public override int? PatientId(LabResult entity) => entity.PatientRecordId;
    public override string Summary(LabResult entity) => entity.AccessionNumber + " | " + entity.Status;
    public override IQueryable<LabResult> Search(IQueryable<LabResult> query, string search) => query.Where(l =>
        EF.Functions.Collate(l.AccessionNumber, "Latin1_General_100_CI_AS").Contains(search) ||
        EF.Functions.Collate(l.TestName, "Latin1_General_100_CI_AS").Contains(search));
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, LabResult entity) =>
        base.Fields(db, entity).Where(f => f.Label is not nameof(LabResult.ReportFileName) and not nameof(LabResult.ReportOriginalFileNameProtected) and not nameof(LabResult.ReportSha256))
            .Select(f => f.Label == nameof(LabResult.AccessionNumber) ? f with { Label = "Report reference" } : f).ToArray();
    public override LabUploadInput Input(ApplicationDbContext db, LabResult entity) => new()
    {
        CollectedAt = entity.CollectedAt.Date,
        ReleasedAt = entity.ReleasedAt, Status = entity.Status
    };
    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData,
        LabUploadInput input, OwnershipContext? context, CancellationToken token)
    {
        var id = viewData["RecordId"] as int?;
        var report = id.HasValue ? await SqlReadRetry.RunAsync(db, () => db.LabResults.AsNoTracking()
            .Where(l => l.Id == id.Value).Select(l => new { HasReport = l.ReportFileName != null, l.ReportSize, l.ReportUploadedAtUtc })
            .SingleOrDefaultAsync(token), token) : null;
        viewData["HasReport"] = report?.HasReport == true;
        viewData["ReportSize"] = report?.ReportSize;
        viewData["ReportUploadedAtUtc"] = report?.ReportUploadedAtUtc;
    }
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, LabResult entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Lab items"] = await db.LabResultItems.CountAsync(i => i.LabResultId == entity.Id, token) };
    public override Task<WriteResult> ApplyAsync(ApplicationDbContext db, LabUploadInput input, LabResult entity,
        OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return Task.FromResult(WriteResult.Invalid("The patient record no longer exists."));
        if (input.Report is not null && !context.Patient.HasHospitalNumber)
            return Task.FromResult(WriteResult.Invalid(MissingHospitalNumberError));
        if (create && input.Report is null) return Task.FromResult(WriteResult.Invalid("Choose the lab report PDF."));
        var collectedAt = !create && entity.CollectedAt.Date == input.CollectedAt!.Value.Date
            ? entity.CollectedAt : WallTime(input.CollectedAt!.Value.Date);
        var releasedAt = input.ReleasedAt is null ? (DateTime?)null : WallTime(input.ReleasedAt.Value, create ? null : entity.ReleasedAt);
        if (releasedAt < collectedAt) return Task.FromResult(WriteResult.Invalid("Release cannot precede collection."));
        if (input.Status == "Available" && input.Report is null && entity.ReportFileName is null)
            return Task.FromResult(WriteResult.Invalid("An attached PDF is required for PDF release."));
        if (create)
        {
            entity.PatientRecordId = context.Patient.Id;
            entity.AccessionNumber = "PORTAL-LAB-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            entity.TestName = "Laboratory report";
            entity.Category = LabCategory.SpecialDiagnostics;
            entity.PerformingUnit = "";
        }
        entity.Status = input.Status;
        entity.CollectedAt = collectedAt;
        entity.ReleasedAt = releasedAt;
        return Task.FromResult(WriteResult.Success);
    }
}
