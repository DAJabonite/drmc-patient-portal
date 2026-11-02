using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class LabEditor : ClinicalEditor<LabResult, LabInput>
{
    public override string Name => "LabResults";
    public override string Title => "Laboratory result";
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, LabResult entity) =>
        base.Fields(db, entity).Where(f => f.Label != nameof(LabResult.ReportFileName)).ToArray();
    public override string SearchColumn => "AccessionNumber";
    public override int? PatientId(LabResult entity) => entity.PatientRecordId;
    public override string Summary(LabResult entity) => entity.TestName + " | " + entity.Status;
    public override IQueryable<LabResult> Search(IQueryable<LabResult> query, string search) => query.Where(l =>
        EF.Functions.Collate(l.AccessionNumber, "Latin1_General_100_CI_AS").Contains(search) || EF.Functions.Collate(l.TestName, "Latin1_General_100_CI_AS").Contains(search));
    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, LabInput input, OwnershipContext? context, CancellationToken token)
    {
        viewData["Doctors"] = await PhysicianNames.OptionsAsync(db, token);
        viewData["Encounters"] = context is null ? [] : await EncounterLinks.OptionsAsync(db, context.Patient.Id, token);
    }
    public override LabInput Input(ApplicationDbContext db, LabResult entity) => new()
    {
        AccessionNumber = entity.AccessionNumber, TestName = entity.TestName, Category = entity.Category, CollectedAt = entity.CollectedAt,
        ReleasedAt = entity.ReleasedAt, Status = entity.Status, ClinicalEncounterId = entity.ClinicalEncounterId, ResultSummary = entity.ResultSummary,
        HistoricalDoctorName = entity.OrderingPhysician, Pathologist = new() { HistoricalDoctorName = entity.PathologistName },
        PerformingUnit = entity.PerformingUnit, ClinicalNotes = entity.ClinicalNotes
    };
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, LabResult entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Lab items"] = await db.LabResultItems.CountAsync(i => i.LabResultId == entity.Id, token) };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, LabInput input, LabResult entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return WriteResult.Invalid("The patient record no longer exists.");
        if (!await EncounterLinks.ValidAsync(db, input.ClinicalEncounterId, context.Patient.Id, create || entity.ClinicalEncounterId.HasValue || input.Report is not null, token, lookups))
            return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
        if ((input.Report is not null || input.Status == "Available") && input.CollectedAt is null)
            return WriteResult.Invalid("Enter the actual test date before uploading or releasing a report.");
        // Structured result values remain a supported import format; a PDF is not required for their status.
        var accession = input.AccessionNumber.Trim().ToUpperInvariant();
        if (lookups is not null ? lookups.Exists("Lab:" + accession) : await db.LabResults.AnyAsync(l => l.Id != entity.Id && EF.Functions.Collate(l.AccessionNumber, "Latin1_General_100_CI_AS") == accession, token))
            return WriteResult.Invalid("A lab already uses this accession number.");
        var ordering = await PhysicianNames.ResolveAsync(db, input, token); var pathologist = await PhysicianNames.ResolveAsync(db, input.Pathologist, token);
        if (ordering is null || pathologist is null) return WriteResult.Invalid("Select existing directory physicians or enter explicit historical names.");
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.AccessionNumber = accession; entity.TestName = input.TestName.Trim(); entity.Category = input.Category;
        entity.CollectedAt = input.CollectedAt is null ? null : WallTime(input.CollectedAt.Value, create ? null : entity.CollectedAt); entity.ReleasedAt = input.ReleasedAt is null ? null : WallTime(input.ReleasedAt.Value, create ? null : entity.ReleasedAt);
        entity.Status = input.Status; entity.ClinicalEncounterId = input.ClinicalEncounterId; entity.ResultSummary = input.ResultSummary ?? "";
        if (input.ClinicalEncounterId.HasValue) entity.RequiresEncounterAssignment = false;
        entity.PatientActivityAtUtc = DateTime.UtcNow;
        entity.OrderingPhysician = ordering; entity.PathologistName = pathologist; entity.PerformingUnit = input.PerformingUnit ?? "";
        entity.ClinicalNotes = input.ClinicalNotes ?? ""; return WriteResult.Success;
    }
}
