using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class PatientEditor : AdminEntity<PatientRecord, PatientInput>
{
    public override string Name => "Patients";
    public override string Title => "Patient registry";
    public override string SearchColumn => "FullName";
    public override bool Clinical => true;
    public override int? PatientId(PatientRecord entity) => entity.Id;
    public override string Summary(PatientRecord entity) => "Hospital number: " + (entity.HospitalNumber ?? "unassigned") +
        (entity.PortalUserId is null ? " | Unlinked" : " | Linked");
    public override IQueryable<PatientRecord> Search(IQueryable<PatientRecord> query, string search) => query.Where(e =>
        EF.Functions.Collate(e.FullName, "Latin1_General_100_CI_AS").Contains(search) ||
        e.HospitalNumber != null && EF.Functions.Collate(e.HospitalNumber, "Latin1_General_100_CI_AS").Contains(search));
    public override PatientInput Input(ApplicationDbContext db, PatientRecord entity) => new()
    { FullName = entity.FullName, DateOfBirth = entity.DateOfBirth, HospitalNumber = entity.HospitalNumber };
    public override Task<OwnershipContext?> RecordContextAsync(ApplicationDbContext db, PatientRecord entity, CancellationToken token) =>
        Task.FromResult<OwnershipContext?>(new(entity, "Patient registry", entity.Id));
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, PatientRecord entity) =>
    [new("Full name", entity.FullName), new("Birth date", entity.DateOfBirth?.ToString("yyyy-MM-dd") ?? "unassigned"),
        new("Hospital number", entity.HospitalNumber ?? "unassigned"), new("Portal account", entity.PortalUserId is null ? "Unlinked" : "Linked")];
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, PatientRecord entity, CancellationToken token) =>
        new Dictionary<string, int>
        {
            ["Encounters"] = await db.ClinicalEncounters.CountAsync(e => e.PatientRecordId == entity.Id, token),
            ["Labs"] = await db.LabResults.CountAsync(e => e.PatientRecordId == entity.Id, token),
            ["Radiology studies"] = await db.RadiologyStudies.CountAsync(e => e.PatientRecordId == entity.Id, token),
            ["Prescriptions"] = await db.Prescriptions.CountAsync(e => e.PatientRecordId == entity.Id, token),
            ["Allergies"] = await db.PatientAllergies.CountAsync(e => e.PatientRecordId == entity.Id, token)
        };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, PatientInput input, PatientRecord entity,
        OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        var number = string.IsNullOrWhiteSpace(input.HospitalNumber) ? null : input.HospitalNumber.Trim();
        if (number is not null && (lookups is not null ? lookups.Exists("Hospital:" + number) : await db.PatientRecords.AnyAsync(e => e.Id != entity.Id && e.HospitalNumber != null &&
            EF.Functions.Collate(e.HospitalNumber, "Latin1_General_100_CI_AS") == number, token)))
            return WriteResult.Invalid("That hospital number is already assigned.");
        if (string.IsNullOrWhiteSpace(input.FullName)) return WriteResult.Invalid("A patient name is required.");
        entity.FullName = input.FullName.Trim(); entity.HospitalNumber = number;
        entity.DateOfBirth = input.DateOfBirth is null ? null : WallTime(input.DateOfBirth.Value.Date);
        return WriteResult.Success;
    }
}
