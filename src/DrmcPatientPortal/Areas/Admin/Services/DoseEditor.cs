using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class DoseEditor : AdminEntity<MedicationDoseSchedule, DoseInput>
{
    public override string Name => "MedicationDoseSchedules";
    public override string Title => "Dose schedules";
    public override string SearchColumn => "DisplayOrder";
    public override bool Clinical => true;
    public override bool NeedsContext => true;
    public override bool RequiresListContext => true;
    public override IQueryable<MedicationDoseSchedule> Query(ApplicationDbContext db, int? contextId = null)
    {
        var query = db.MedicationDoseSchedules.Include(d => d.Prescription).AsQueryable();
        return contextId is null ? query : query.Where(d => d.PrescriptionId == contextId);
    }
    public override IQueryable<MedicationDoseSchedule> Search(IQueryable<MedicationDoseSchedule> query, string search)
    {
        var time = DoseInput.Parse(search);
        return time is null ? query.Where(d => EF.Functions.Collate(d.DisplayOrder.ToString(), "Latin1_General_100_CI_AS").Contains(search)) : query.Where(d => d.DoseTime == time);
    }
    public override string Label(ApplicationDbContext db, MedicationDoseSchedule entity) => entity.DoseTime.ToString("HH:mm:ss.fffffff");
    public override string Summary(MedicationDoseSchedule entity) => "Display order: " + entity.DisplayOrder;
    public override int? PatientId(MedicationDoseSchedule entity) => entity.Prescription.PatientRecordId;
    public override async Task<OwnershipContext?> CreateContextAsync(ApplicationDbContext db, int routeId, CancellationToken token)
    {
        var rx = await db.Prescriptions.Include(p => p.Patient).SingleOrDefaultAsync(p => p.Id == routeId, token);
        return rx is null ? null : new(rx.Patient, "Prescription: " + rx.RxNumber + " | " + rx.GenericName, rx.Id);
    }
    public override Task<OwnershipContext?> RecordContextAsync(ApplicationDbContext db, MedicationDoseSchedule entity, CancellationToken token) => CreateContextAsync(db, entity.PrescriptionId, token);
    public override async Task<AdminList> SelectContextAsync(ApplicationDbContext db, string? search, int page, CancellationToken token)
    {
        var query = db.Prescriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => EF.Functions.Collate(p.RxNumber, "Latin1_General_100_CI_AS").Contains(search) ||
            EF.Functions.Collate(p.GenericName, "Latin1_General_100_CI_AS").Contains(search));
        var count = await query.CountAsync(token); page = Math.Clamp(page, 1, Math.Max(1, (count + 24) / 25));
        var prescriptions = await query.OrderByDescending(p => p.Id).Skip((page - 1) * 25).Take(25).ToListAsync(token);
        return new("Select prescription", prescriptions.Select(p => new AdminRow(p.Id, p.RxNumber, p.GenericName + " | Patient #" + p.PatientRecordId, p.PatientRecordId)).ToArray(), search, page, count);
    }
    public override DoseInput Input(ApplicationDbContext db, MedicationDoseSchedule entity) => new()
    { DoseTime = entity.DoseTime.ToString("HH:mm:ss.fff"), DisplayOrder = entity.DisplayOrder };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, DoseInput input, MedicationDoseSchedule entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        if (context is null) return WriteResult.Invalid("The parent prescription no longer exists.");
        var parent = lookups is not null ? lookups.Prescriptions.GetValueOrDefault(context.RouteId) : db.Prescriptions.Local.SingleOrDefault(p => db.Entry(p).State == EntityState.Added && p.Id == context.RouteId && p.PatientRecordId == context.Patient.Id) ??
            await db.Prescriptions.SingleOrDefaultAsync(p => p.Id == context.RouteId && p.PatientRecordId == context.Patient.Id, token);
        if (parent is null || parent.PatientRecordId != context.Patient.Id) return WriteResult.Invalid("The prescription does not belong to the selected patient.");
        var time = DoseInput.Parse(input.DoseTime);
        if (time is null) return WriteResult.Invalid("Enter a valid Manila dose time.");
        if (lookups is not null ? lookups.Exists("Dose:" + parent.Id + ":" + time.Value.Ticks) : await db.MedicationDoseSchedules.AnyAsync(d => d.PrescriptionId == parent.Id && d.Id != entity.Id && d.DoseTime == time, token))
            return WriteResult.Invalid("This prescription already has a schedule at that time.");
        if (create) { entity.PrescriptionId = parent.Id; entity.Prescription = parent; }
        else if (entity.PrescriptionId != parent.Id) return WriteResult.Invalid("Dose schedule ownership cannot change.");
        var value = time.Value;
        if (!create && value.Ticks % TimeSpan.TicksPerMillisecond == 0 &&
            value.Ticks / TimeSpan.TicksPerMillisecond == entity.DoseTime.Ticks / TimeSpan.TicksPerMillisecond)
            value = entity.DoseTime;
        entity.DoseTime = value; entity.DisplayOrder = input.DisplayOrder!.Value; return WriteResult.Success;
    }
}
