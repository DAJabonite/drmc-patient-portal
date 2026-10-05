using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class PrescriptionEditor : ClinicalEditor<Prescription, PrescriptionInput>
{
    public override string Name => "Prescriptions";
    public override string Title => "Prescriptions";
    public override string SearchColumn => "RxNumber";
    public override int? PatientId(Prescription entity) => entity.PatientRecordId;
    public override string Summary(Prescription entity) => entity.GenericName + " | " + entity.Status;
    public override IQueryable<Prescription> Search(IQueryable<Prescription> query, string search) => query.Where(p =>
        EF.Functions.Collate(p.RxNumber, "Latin1_General_100_CI_AS").Contains(search) || EF.Functions.Collate(p.GenericName, "Latin1_General_100_CI_AS").Contains(search));
    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, PrescriptionInput input, OwnershipContext? context, CancellationToken token) =>
        viewData["Doctors"] = await PhysicianNames.OptionsAsync(db, token);
    public override PrescriptionInput Input(ApplicationDbContext db, Prescription entity) => new()
    {
        RxNumber = entity.RxNumber, GenericName = entity.GenericName, BrandName = entity.BrandName, Dosage = entity.Dosage,
        DosageForm = entity.DosageForm, Frequency = entity.Frequency, Instructions = entity.Instructions, Department = entity.Department,
        HistoricalDoctorName = entity.PrescribingDoctor, PrescribedAt = entity.PrescribedAt, ValidUntil = entity.ValidUntil, Status = entity.Status,
        RefillsTotal = entity.RefillsTotal, RefillsRemaining = entity.RefillsRemaining, LastRefillDate = entity.LastRefillDate
    };
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, Prescription entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Dose schedules"] = await db.MedicationDoseSchedules.CountAsync(d => d.PrescriptionId == entity.Id, token) };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, PrescriptionInput input, Prescription entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return WriteResult.Invalid("The patient record no longer exists.");
        var number = input.RxNumber.Trim().ToUpperInvariant();
        if (lookups is not null ? lookups.Exists("Rx:" + number) : await db.Prescriptions.AnyAsync(p => p.Id != entity.Id && EF.Functions.Collate(p.RxNumber, "Latin1_General_100_CI_AS") == number, token))
            return WriteResult.Invalid("A prescription already uses this Rx number.");
        var physician = await PhysicianNames.ResolveAsync(db, input, token);
        if (physician is null) return WriteResult.Invalid("Select an existing directory physician or enter an explicit historical name.");
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.RxNumber = number; entity.GenericName = input.GenericName.Trim(); entity.BrandName = input.BrandName;
        entity.Dosage = input.Dosage ?? ""; entity.DosageForm = input.DosageForm ?? ""; entity.Frequency = input.Frequency ?? "";
        entity.Instructions = input.Instructions ?? ""; entity.Department = input.Department; entity.PrescribingDoctor = physician;
        entity.PrescribedAt = WallTime(input.PrescribedAt!.Value, create ? null : entity.PrescribedAt); entity.ValidUntil = WallTime(input.ValidUntil!.Value, create ? null : entity.ValidUntil); entity.Status = input.Status;
        entity.RefillsTotal = input.RefillsTotal!.Value; entity.RefillsRemaining = input.RefillsRemaining!.Value;
        entity.LastRefillDate = input.LastRefillDate is null ? null : WallTime(input.LastRefillDate.Value, create ? null : entity.LastRefillDate); return WriteResult.Success;
    }
}
