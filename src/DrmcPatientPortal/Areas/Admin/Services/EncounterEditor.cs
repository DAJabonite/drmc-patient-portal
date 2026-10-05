using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class EncounterEditor : ClinicalEditor<ClinicalEncounter, EncounterInput>
{
    public override string Name => "ClinicalEncounters";
    public override string Title => "Clinical encounters";
    public override string SearchColumn => "EncounterReference";
    public override int? PatientId(ClinicalEncounter entity) => entity.PatientRecordId;
    public override string Summary(ClinicalEncounter entity) => entity.EncounterDate.ToString("yyyy-MM-dd HH:mm") + " | " + entity.Department;
    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, EncounterInput input, OwnershipContext? context, CancellationToken token) =>
        viewData["Doctors"] = await PhysicianNames.OptionsAsync(db, token);
    public override EncounterInput Input(ApplicationDbContext db, ClinicalEncounter entity) => new()
    {
        EncounterReference = entity.EncounterReference, EncounterDate = entity.EncounterDate, Department = entity.Department,
        Type = entity.Type, HistoricalDoctorName = entity.AttendingPhysician, ChiefComplaint = entity.ChiefComplaint,
        PrimaryDiagnosis = entity.PrimaryDiagnosis, SecondaryDiagnosis = entity.SecondaryDiagnosis, ClinicalSummary = entity.ClinicalSummary,
        CarePlanAndInstructions = entity.CarePlanAndInstructions, VitalSignsRecorded = entity.VitalSignsRecorded,
        FollowUpDate = entity.FollowUpDate, FollowUpNotes = entity.FollowUpNotes
    };
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, ClinicalEncounter entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Labs"] = await db.LabResults.CountAsync(l => l.ClinicalEncounterId == entity.Id, token) };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, EncounterInput input, ClinicalEncounter entity,
        OwnershipContext? context, bool create, CancellationToken token)
    {
        if (context is null) return WriteResult.Invalid("The patient record no longer exists.");
        var reference = input.EncounterReference.Trim().ToUpperInvariant();
        if (await db.ClinicalEncounters.AnyAsync(e => e.Id != entity.Id && EF.Functions.Collate(e.EncounterReference, "Latin1_General_100_CI_AS") == reference, token))
            return WriteResult.Invalid("An encounter already uses this reference.");
        var physician = await PhysicianNames.ResolveAsync(db, input, token);
        if (physician is null) return WriteResult.Invalid("Select an existing directory physician or enter an explicit historical name.");
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.EncounterReference = reference; entity.EncounterDate = WallTime(input.EncounterDate!.Value, create ? null : entity.EncounterDate);
        entity.Department = input.Department; entity.Type = input.Type; entity.AttendingPhysician = physician;
        entity.ChiefComplaint = input.ChiefComplaint ?? ""; entity.PrimaryDiagnosis = input.PrimaryDiagnosis ?? "";
        entity.SecondaryDiagnosis = input.SecondaryDiagnosis; entity.ClinicalSummary = input.ClinicalSummary ?? "";
        entity.CarePlanAndInstructions = input.CarePlanAndInstructions ?? ""; entity.VitalSignsRecorded = input.VitalSignsRecorded ?? "";
        entity.FollowUpDate = input.FollowUpDate is null ? null : WallTime(input.FollowUpDate.Value, create ? null : entity.FollowUpDate); entity.FollowUpNotes = input.FollowUpNotes ?? "";
        return WriteResult.Success;
    }
}
