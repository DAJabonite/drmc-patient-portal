using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class AllergyEditor : ClinicalEditor<PatientAllergy, AllergyInput>
{
    public override string Name => "PatientAllergies";
    public override string Title => "Patient allergies";
    public override string SearchColumn => "Allergen";
    public override int? PatientId(PatientAllergy entity) => entity.PatientRecordId;
    public override string Summary(PatientAllergy entity) => entity.Severity + " | " + entity.RecordedAt.ToString("yyyy-MM-dd HH:mm");
    public override AllergyInput Input(ApplicationDbContext db, PatientAllergy entity) => new()
    { Allergen = entity.Allergen, Reaction = entity.Reaction, Severity = entity.Severity, RecordedAt = entity.RecordedAt };
    public override Task<WriteResult> ApplyAsync(ApplicationDbContext db, AllergyInput input, PatientAllergy entity, OwnershipContext? context, bool create, CancellationToken token)
    {
        if (context is null) return Task.FromResult(WriteResult.Invalid("The patient record no longer exists."));
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.Allergen = input.Allergen.Trim(); entity.Reaction = input.Reaction ?? ""; entity.Severity = input.Severity;
        entity.RecordedAt = WallTime(input.RecordedAt!.Value, create ? null : entity.RecordedAt); return Task.FromResult(WriteResult.Success);
    }
}
