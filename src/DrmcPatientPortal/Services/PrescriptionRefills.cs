using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class PrescriptionRefills
{
    public static Task SynchronizeClinicPatientAsync(ApplicationDbContext db, int clinicPatientId, CancellationToken token = default) =>
        SynchronizePatientsAsync(db, [clinicPatientId], token);

    public static async Task SynchronizePatientsAsync(ApplicationDbContext db, IEnumerable<int> clinicPatientIds, CancellationToken token = default)
    {
        foreach (var chunk in clinicPatientIds.Distinct().Chunk(100))
        {
            var prescriptions = await db.Prescriptions.Where(p => p.RedStarEnrollmentId != null &&
                EF.Parameter(chunk).Contains(p.RedStarEnrollment!.PatientId)).OrderBy(p => p.Id).ToListAsync(token);
            foreach (var prescription in prescriptions) await SynchronizeAsync(db, prescription, token);
        }
        await db.SaveChangesAsync(token);
    }

    // SQL narrows the candidates; the protected identity fingerprint is checked in the application.
    public static IQueryable<RedStarAppointment> EligibleQuery(ApplicationDbContext db) =>
        db.RedStarAppointments.Where(a => a.Kind == RedStarAppointmentKind.Refill &&
            a.State == RedStarAppointmentState.Scheduled && a.RemindersVerified && a.PrescriptionId != null &&
            a.Prescription!.IsRedStar && a.Prescription.Status == PrescriptionStatus.Active &&
            a.Prescription.FulfillmentStatus == PrescriptionFulfillmentStatus.Served &&
            a.Prescription.NextRefillDate != null && a.Prescription.NextRefillDate == a.DueDate &&
            a.Prescription.RedStarEnrollmentId == a.EnrollmentId &&
            a.Prescription.PatientRecordId == a.Enrollment.Patient.HospitalPatientRecordId &&
            a.Prescription.ClinicalEncounterId != null && a.Prescription.Encounter!.PatientRecordId == a.Prescription.PatientRecordId &&
            a.Enrollment.Status == RedStarStatus.Active && a.Enrollment.Program != RedStarProgram.Unverified);

    public static async Task<bool> HasValidMappingAsync(ApplicationDbContext db, Prescription rx, CancellationToken token = default)
    {
        if (!rx.IsRedStar || rx.RedStarEnrollmentId is not int enrollmentId) return false;
        var enrollment = await db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient)
            .SingleOrDefaultAsync(e => e.Id == enrollmentId, token);
        if (enrollment is null) return false;
        var hospital = await db.PatientRecords.AsNoTracking().SingleOrDefaultAsync(p => p.Id == rx.PatientRecordId, token);
        return hospital is not null && RedStarReportAccess.MappingValid(enrollment.Patient, hospital);
    }

    public static async Task SynchronizeAsync(ApplicationDbContext db, Prescription rx, CancellationToken token = default)
    {
        var appointment = db.RedStarAppointments.Local.FirstOrDefault(a => ReferenceEquals(a.Prescription, rx) ||
            rx.Id > 0 && a.PrescriptionId == rx.Id);
        if (appointment is null && rx.Id > 0)
            appointment = await db.RedStarAppointments.SingleOrDefaultAsync(a => a.PrescriptionId == rx.Id, token);

        var enrollment = rx.IsRedStar && rx.RedStarEnrollmentId.HasValue
            ? await db.RedStarEnrollments.AsNoTracking().SingleOrDefaultAsync(e => e.Id == rx.RedStarEnrollmentId, token) : null;
        var eligible = rx.IsRedStar && rx.Status == PrescriptionStatus.Active &&
            rx.FulfillmentStatus == PrescriptionFulfillmentStatus.Served && rx.NextRefillDate.HasValue &&
            rx.ClinicalEncounterId.HasValue && enrollment is not null && enrollment.Status == RedStarStatus.Active &&
            enrollment.Program != RedStarProgram.Unverified && await HasValidMappingAsync(db, rx, token);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Eligible = eligible, rx.IsRedStar, rx.PatientRecordId, rx.RedStarEnrollmentId,
            rx.Status, rx.FulfillmentStatus, rx.FrequencyMode,
            Frequency = rx.FrequencyValue?.ToString("0.####", CultureInfo.InvariantCulture),
            Dose = rx.DosePerIntake?.ToString("0.####", CultureInfo.InvariantCulture),
            Dispensed = rx.DispensedQuantity?.ToString("0.####", CultureInfo.InvariantCulture),
            ServedDate = rx.ServedDate?.Date, DueDate = rx.NextRefillDate?.Date
        })));
        if (appointment is null)
        {
            if (!eligible) return;
            db.RedStarAppointments.Add(new RedStarAppointment
            {
                EnrollmentId = rx.RedStarEnrollmentId!.Value, Prescription = rx,
                DueDate = rx.NextRefillDate!.Value.Date, Kind = RedStarAppointmentKind.Refill,
                State = RedStarAppointmentState.Scheduled, RemindersVerified = true,
                PrescriptionScheduleFingerprint = fingerprint
            });
            return;
        }
        if (appointment.PrescriptionScheduleFingerprint == fingerprint && appointment.RemindersVerified == eligible &&
            appointment.State == (eligible ? RedStarAppointmentState.Scheduled : RedStarAppointmentState.Cancelled)) return;
        if (appointment.Id > 0)
        {
            await RedStarAuditTrail.CancelRemindersAsync(db, db.RedStarAppointments.Where(a => a.Id == appointment.Id), token);
            await db.RedStarRescheduleRequests.Where(r => r.AppointmentId == appointment.Id && r.State == RedStarRequestState.Pending)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, RedStarRequestState.Declined), token);
        }
        appointment.Revision++;
        appointment.ConfirmedUtc = null;
        appointment.PrescriptionScheduleFingerprint = fingerprint;
        appointment.RemindersVerified = eligible;
        appointment.State = eligible ? RedStarAppointmentState.Scheduled : RedStarAppointmentState.Cancelled;
        if (eligible)
        {
            appointment.EnrollmentId = rx.RedStarEnrollmentId!.Value;
            appointment.DueDate = rx.NextRefillDate!.Value.Date;
        }
    }
}
