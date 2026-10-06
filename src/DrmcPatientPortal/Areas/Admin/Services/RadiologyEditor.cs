using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
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
        .Select(f => f.Label switch
        {
            nameof(RadiologyStudy.Modality) => f with { Value = entity.Modality.DisplayName() },
            nameof(RadiologyStudy.Status) => f with { Value = entity.Status.DisplayName() },
            _ => f
        }).ToArray();

    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, RadiologyInput input, OwnershipContext? context, CancellationToken token)
    {
        viewData["Doctors"] = await PhysicianNames.OptionsAsync(db, token);
        viewData["Encounters"] = await db.ClinicalEncounters.Where(e => e.PatientRecordId == context!.Patient.Id)
            .OrderByDescending(e => e.EncounterDate).ThenBy(e => e.Id).Select(e => new SelectListItem(e.EncounterReference, e.Id.ToString())).ToListAsync(token);
    }

    public override RadiologyInput Input(ApplicationDbContext db, RadiologyStudy entity) => new()
    {
        AccessionNumber = entity.AccessionNumber, StudyName = entity.StudyName, Modality = entity.Modality, BodyRegion = entity.BodyRegion,
        PerformedAt = entity.PerformedAt, ReleasedAt = entity.ReleasedAt, Status = entity.Status, ClinicalEncounterId = entity.ClinicalEncounterId,
        HistoricalDoctorName = entity.OrderingPhysician, Radiologist = new() { HistoricalDoctorName = entity.RadiologistName },
        PerformingUnit = entity.PerformingUnit, ClinicalIndication = entity.ClinicalIndication, Technique = entity.Technique,
        Comparison = entity.Comparison, Findings = entity.Findings, Impression = entity.Impression,
        PlainLanguageSummary = entity.PlainLanguageSummary, AmendmentNote = entity.AmendmentNote, InternalNotes = entity.InternalNotes
    };

    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, RadiologyInput input, RadiologyStudy entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return WriteResult.Invalid("The patient record no longer exists.");
        if (input.ClinicalEncounterId is not null && !await db.ClinicalEncounters.AnyAsync(e => e.Id == input.ClinicalEncounterId && e.PatientRecordId == context.Patient.Id, token))
            return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
        var accession = input.AccessionNumber.Trim().ToUpperInvariant();
        if (accession.Length == 0) return WriteResult.Invalid("An accession number is required.");
        if (await db.RadiologyStudies.AnyAsync(r => r.Id != entity.Id && EF.Functions.Collate(r.AccessionNumber, "Latin1_General_100_CI_AS") == accession, token))
            return WriteResult.Invalid("A radiology study already uses this accession number.");
        var ordering = await PhysicianNames.ResolveAsync(db, input, token); var radiologist = await PhysicianNames.ResolveAsync(db, input.Radiologist, token);
        if (ordering is null || radiologist is null) return WriteResult.Invalid("Select existing directory physicians or enter explicit historical names.");
        if (ordering.Length > RadiologyStudy.NameLength || radiologist.Length > RadiologyStudy.NameLength)
            return WriteResult.Invalid($"Physician names must be at most {RadiologyStudy.NameLength} characters.");
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.AccessionNumber = accession; entity.StudyName = input.StudyName.Trim(); entity.Modality = input.Modality; entity.BodyRegion = input.BodyRegion.Trim();
        entity.PerformedAt = WallTime(input.PerformedAt!.Value, create ? null : entity.PerformedAt);
        entity.ReleasedAt = input.ReleasedAt is null ? null : WallTime(input.ReleasedAt.Value, create ? null : entity.ReleasedAt);
        entity.Status = input.Status; entity.ClinicalEncounterId = input.ClinicalEncounterId;
        entity.OrderingPhysician = ordering; entity.RadiologistName = radiologist; entity.PerformingUnit = input.PerformingUnit?.Trim() ?? "";
        entity.ClinicalIndication = input.ClinicalIndication?.Trim() ?? ""; entity.Technique = input.Technique?.Trim() ?? "";
        entity.Comparison = input.Comparison?.Trim() ?? ""; entity.Findings = input.Findings?.Trim() ?? ""; entity.Impression = input.Impression?.Trim() ?? "";
        entity.PlainLanguageSummary = input.PlainLanguageSummary?.Trim() ?? ""; entity.AmendmentNote = input.AmendmentNote?.Trim() ?? "";
        entity.InternalNotes = input.InternalNotes?.Trim() ?? "";
        return WriteResult.Success;
    }
}
