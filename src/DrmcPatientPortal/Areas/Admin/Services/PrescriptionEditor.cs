using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class PrescriptionEditor(PrescriptionAccess access) : ClinicalEditor<Prescription, PrescriptionInput>
{
    public override string Name => "Prescriptions";
    public override string Title => "Prescriptions";
    public override string SearchColumn => "RxNumber";
    public override int? PatientId(Prescription entity) => entity.PatientRecordId;
    public override string Summary(Prescription entity) => entity.GenericName + " | " + entity.Status;
    public override IReadOnlyList<RecordField> Fields(ApplicationDbContext db, Prescription entity) => base.Fields(db, entity)
        .Where(f => f.Label is not "RefillsTotal" and not "RefillsRemaining" and not "LastRefillDate" and not "PhiProtectionVersion").ToArray();
    public override IQueryable<Prescription> Query(ApplicationDbContext db, int? contextId = null) =>
        contextId.HasValue ? access.Query(db).Where(p => p.PatientRecordId == contextId) : access.Query(db);
    public override Task<IQueryable<Prescription>> SearchAsync(IQueryable<Prescription> query, string search, CancellationToken token) => PrescriptionQueries.SearchAsync(query, search, token);
    public override async Task<OwnershipContext?> CreateContextAsync(ApplicationDbContext db, int routeId, CancellationToken token)
    {
        var patient = await access.Patients(db).SingleOrDefaultAsync(p => p.Id == routeId, token);
        return patient is null ? null : new(patient, Title, patient.Id);
    }
    public override async Task<AdminList> SelectContextAsync(ApplicationDbContext db, string? search, int page, CancellationToken token)
    {
        var query = access.Patients(db);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.FullName.Contains(search) || p.HospitalNumber != null && p.HospitalNumber.Contains(search));
        var total = await query.CountAsync(token); page = Math.Clamp(page, 1, Math.Max(1, (total + 24) / 25));
        var patients = await query.OrderBy(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        return new("Select patient", patients.Select(p => new AdminRow(p.Id, p.FullName, p.HospitalNumber ?? "", p.Id)).ToArray(), search, page, total);
    }
    public override async Task ConfigureFormAsync(ApplicationDbContext db, ViewDataDictionary viewData, PrescriptionInput input, OwnershipContext? context, CancellationToken token)
    {
        viewData["Doctors"] = await PhysicianNames.OptionsAsync(db, token);
        viewData["Encounters"] = context is null ? [] : await EncounterLinks.OptionsAsync(db, context.Patient.Id, token);
        viewData["RedStarOnly"] = !access.IsAdmin;
        if (!access.IsAdmin) input.IsRedStar = true;
        var enrollments = context is null ? [] : await access.EnrollmentsForAsync(db, context.Patient.Id, token);
        viewData["RedStarEnrollments"] = enrollments
            .Select(e => new SelectListItem(e.Program.ToString(), e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    }
    public override PrescriptionInput Input(ApplicationDbContext db, Prescription entity) => new()
    {
        ClinicalEncounterId = entity.ClinicalEncounterId,
        RxNumber = entity.RxNumber, GenericName = entity.GenericName, BrandName = entity.BrandName, Dosage = entity.Dosage,
        DosageForm = entity.DosageForm, RouteOfAdministration = entity.RouteOfAdministration,
        Frequency = entity.Frequency, Instructions = entity.Instructions, Department = entity.Department,
        HistoricalDoctorName = entity.PrescribingDoctor, PrescribedAt = entity.PrescribedAt, ValidUntil = entity.ValidUntil, Status = entity.Status,
        FulfillmentStatus = entity.FulfillmentStatus, FrequencyMode = entity.FrequencyMode, FrequencyValue = entity.FrequencyValue,
        DosePerIntake = entity.DosePerIntake, DoseUnit = entity.DoseUnit, DurationCount = entity.DurationCount, DurationUnit = entity.DurationUnit,
        PrescribedQuantity = entity.PrescribedQuantity, DispensedQuantity = entity.DispensedQuantity, ServedDate = entity.ServedDate,
        IsRedStar = entity.IsRedStar, RedStarEnrollmentId = entity.RedStarEnrollmentId
    };
    public override async Task<IReadOnlyDictionary<string, int>> DependentsAsync(ApplicationDbContext db, Prescription entity, CancellationToken token) =>
        new Dictionary<string, int> { ["Dose schedules"] = await db.MedicationDoseSchedules.CountAsync(d => d.PrescriptionId == entity.Id, token),
            ["Rx_RefillHistory"] = await db.RedStarAppointments.CountAsync(a => a.PrescriptionId == entity.Id, token) };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, PrescriptionInput input, Prescription entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        var canWrite = lookups is null ? await access.InitializeAsync(db, token) : access.IsAdmin;
        if (!canWrite || !access.IsAdmin && !input.IsRedStar) return WriteResult.Invalid("Rx_AccessDenied");
        if (context is null) return WriteResult.Invalid("The patient record no longer exists.");
        if (!access.IsAdmin && !await access.Patients(db).AnyAsync(p => p.Id == context.Patient.Id, token)) return WriteResult.Invalid("Rx_AccessDenied");
        if (input.IsRedStar && !(await access.EnrollmentsForAsync(db, context.Patient.Id, token)).Any(e => e.Id == input.RedStarEnrollmentId))
            return WriteResult.Invalid("Rx_InvalidEnrollment");
        if (!Enum.IsDefined(input.Status) || !Enum.IsDefined(input.FulfillmentStatus)) return WriteResult.Invalid("Rx_InvalidRegimen");
        var legacy = !create && !input.IsRedStar && entity.FrequencyMode is null && !PrescriptionCalculations.HasRegimen(input);
        if (legacy && (input.FulfillmentStatus != PrescriptionFulfillmentStatus.Prescribed || input.DispensedQuantity.HasValue || input.ServedDate.HasValue))
            return WriteResult.Invalid("Rx_InvalidRegimen");
        var calculation = legacy ? new PrescriptionCalculation(null, null) : PrescriptionCalculations.Calculate(input, ClinicalClock.Today);
        if (!calculation.Succeeded) return WriteResult.Invalid(calculation.Error!);
        if (!await EncounterLinks.ValidAsync(db, input.ClinicalEncounterId, context.Patient.Id, create || input.IsRedStar || entity.ClinicalEncounterId.HasValue, token, lookups))
            return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
        if (input.RouteOfAdministration is not null && !AdministrationRoutes.All.Contains(input.RouteOfAdministration, StringComparer.Ordinal))
            return WriteResult.Invalid("Select a listed route of administration.");
        var number = input.RxNumber.Trim().ToUpperInvariant();
        if (lookups is not null ? lookups.Exists("Rx:" + number) : await db.Prescriptions.AnyAsync(p => p.Id != entity.Id && EF.Functions.Collate(p.RxNumber, "Latin1_General_100_CI_AS") == number, token))
            return WriteResult.Invalid("A prescription already uses this Rx number.");
        var physician = await PhysicianNames.ResolveAsync(db, input, token);
        if (physician is null) return WriteResult.Invalid("Select an existing directory physician or enter an explicit historical name.");
        if (create) entity.PatientRecordId = context.Patient.Id;
        entity.ClinicalEncounterId = input.ClinicalEncounterId;
        entity.PatientActivityAtUtc = DateTime.UtcNow;
        entity.RxNumber = number; entity.GenericName = input.GenericName.Trim(); entity.BrandName = input.BrandName;
        entity.Dosage = input.Dosage ?? ""; entity.DosageForm = input.DosageForm ?? ""; entity.Frequency = input.Frequency ?? "";
        entity.RouteOfAdministration = input.RouteOfAdministration;
        entity.Instructions = input.Instructions ?? ""; entity.Department = input.Department; entity.PrescribingDoctor = physician;
        entity.PrescribedAt = WallTime(input.PrescribedAt!.Value, create ? null : entity.PrescribedAt); entity.ValidUntil = WallTime(input.ValidUntil!.Value, create ? null : entity.ValidUntil); entity.Status = input.Status;
        entity.FulfillmentStatus = input.FulfillmentStatus; entity.FrequencyMode = input.FrequencyMode;
        entity.FrequencyValue = input.FrequencyValue; entity.DosePerIntake = input.DosePerIntake; entity.DoseUnit = input.DoseUnit?.Trim();
        entity.DurationCount = input.DurationCount; entity.DurationUnit = input.DurationUnit; entity.PrescribedQuantity = calculation.Quantity;
        entity.DispensedQuantity = input.FulfillmentStatus == PrescriptionFulfillmentStatus.Served ? input.DispensedQuantity : null;
        entity.ServedDate = input.FulfillmentStatus == PrescriptionFulfillmentStatus.Served ? input.ServedDate?.Date : null;
        entity.NextRefillDate = calculation.NextRefillDate; entity.IsRedStar = input.IsRedStar;
        entity.RedStarEnrollmentId = input.IsRedStar ? input.RedStarEnrollmentId : null;
        if (create) { entity.RefillsTotal = 0; entity.RefillsRemaining = 0; entity.LastRefillDate = null; }
        await PrescriptionRefills.SynchronizeAsync(db, entity, token);
        return WriteResult.Success;
    }
}
