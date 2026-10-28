using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class RadiologyEditor : ClinicalEditor<RadiologyStudy, RadiologyInput>
{
    public override string Name => "RadiologyStudies";
    public override string Title => "Radiology studies";
    public override string SearchColumn => "AccessionNumber";
    public override int? PatientId(RadiologyStudy entity) => entity.PatientRecordId;
    public override string Summary(RadiologyStudy entity) => entity.StudyName + " | " + entity.Modality.DisplayName() + " | " + entity.Status.DisplayName();
    public override IQueryable<RadiologyStudy> Search(IQueryable<RadiologyStudy> query, string search) => query.Where(r =>
        EF.Functions.Collate(r.AccessionNumber, "Latin1_General_100_CI_AS").Contains(search) || EF.Functions.Collate(r.StudyName, "Latin1_General_100_CI_AS").Contains(search));

    // Enum identifiers are never rendered; show display names instead.
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, RadiologyStudy entity) => base.Fields(db, entity)
        .Where(f => f.Label is not nameof(RadiologyStudy.ReportFileName) and not nameof(RadiologyStudy.ReportOriginalFileNameProtected) and not nameof(RadiologyStudy.ReportSha256))
        .Select(f => f.Label switch
        {
            nameof(RadiologyStudy.Modality) => f with { Value = entity.Modality.DisplayName() },
            nameof(RadiologyStudy.Status) => f with { Value = entity.Status.DisplayName() },
            _ => f
        }).ToArray();

    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, RadiologyInput input, OwnershipContext? context, CancellationToken token)
    {
        var id = viewData["RecordId"] as int?;
        var report = id.HasValue ? await SqlReadRetry.RunAsync(db, () => db.RadiologyStudies.AsNoTracking()
            .Where(s => s.Id == id.Value).Select(s => new { HasReport = s.ReportFileName != null, s.ReportSize, s.ReportUploadedAtUtc })
            .SingleOrDefaultAsync(token), token) : null;
        viewData["HasReport"] = report?.HasReport == true;
        viewData["ReportSize"] = report?.ReportSize;
        viewData["ReportUploadedAtUtc"] = report?.ReportUploadedAtUtc;
    }

    public override RadiologyInput Input(ApplicationDbContext db, RadiologyStudy entity) => new()
    {
        Modality = entity.Modality, PerformedAt = entity.PerformedAt.Date, ReleasedAt = entity.ReleasedAt,
        Status = entity.Status, AmendmentNote = entity.AmendmentNote
    };

    public override Task<WriteResult> ApplyAsync(ApplicationDbContext db, RadiologyInput input, RadiologyStudy entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return Task.FromResult(WriteResult.Invalid("The patient record no longer exists."));
        if (input.Report is not null && !context.Patient.HasHospitalNumber)
            return Task.FromResult(WriteResult.Invalid(LabUploadEditor.MissingHospitalNumberError));
        if (create && input.Report is null) return Task.FromResult(WriteResult.Invalid("Choose the radiology report PDF."));
        if (input.Status is RadiologyStatus.Final or RadiologyStatus.Amended && entity.ReportFileName is null && input.Report is null)
            return Task.FromResult(WriteResult.Invalid("An attached PDF is required for PDF release."));
        var performedAt = !create && entity.PerformedAt.Date == input.PerformedAt!.Value.Date
            ? entity.PerformedAt : WallTime(input.PerformedAt!.Value.Date);
        var releasedAt = input.ReleasedAt is null ? (DateTime?)null : WallTime(input.ReleasedAt.Value, create ? null : entity.ReleasedAt);
        if (releasedAt < performedAt) return Task.FromResult(WriteResult.Invalid("Release cannot precede the exam."));
        if (create)
        {
            entity.PatientRecordId = context.Patient.Id;
            entity.AccessionNumber = "PORTAL-RAD-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            entity.StudyName = "Radiology report";
        }
        entity.Modality = input.Modality;
        entity.PerformedAt = performedAt;
        entity.ReleasedAt = releasedAt;
        entity.Status = input.Status;
        if (input.Status == RadiologyStatus.Amended) entity.AmendmentNote = input.AmendmentNote!.Trim();
        return Task.FromResult(WriteResult.Success);
    }
}
