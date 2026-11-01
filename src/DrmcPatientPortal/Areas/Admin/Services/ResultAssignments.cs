using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class ResultAssignments
{
    public static async Task<WriteResult> LaboratoryAsync(ApplicationDbContext db, ILabReportStorage storage, int id,
        ResultAssignmentInput input, ClaimsPrincipal user, CancellationToken token)
    {
        var version = AdminAudit.Version(input.RowVersion);
        var source = await db.LabResults.SingleOrDefaultAsync(l => l.Id == id, token);
        if (source is null || version is null || !version.SequenceEqual(db.Entry(source).Property<byte[]>("RowVersion").CurrentValue!)) return WriteResult.Stale;
        if (!source.RequiresEncounterAssignment || source.ReportFileName is null || source.CollectedAt is null)
            return WriteResult.Invalid("This report is no longer waiting for encounter assignment.");
        if (!await EncounterLinks.ValidAsync(db, input.ClinicalEncounterId, source.PatientRecordId, true, token))
            return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
        if (string.IsNullOrWhiteSpace(input.ExistingRequest))
        {
            source.ClinicalEncounterId = input.ClinicalEncounterId; source.RequiresEncounterAssignment = false;
            source.PatientActivityAtUtc = DateTime.UtcNow;
            db.Entry(source).Property("RowVersion").OriginalValue = version;
            await AdminAudit.AddAsync(db, user, "Assign encounter", "LabResults", id.ToString(System.Globalization.CultureInfo.InvariantCulture), source.PatientRecordId,
                fields: [nameof(source.ClinicalEncounterId), nameof(source.RequiresEncounterAssignment)], cancellationToken: token);
            return WriteResult.Success;
        }
        var key = PendingRequestKeys.Parse(input.ExistingRequest);
        if (key is null) return WriteResult.Invalid("Reload and select an existing pending request.");
        var target = await db.LabResults.SingleOrDefaultAsync(l => l.Id == key.Value.Id, token);
        if (target is null || !key.Value.Version.SequenceEqual(db.Entry(target).Property<byte[]>("RowVersion").CurrentValue!)) return WriteResult.Stale;
        if (target.Id == id || target.PatientRecordId != source.PatientRecordId || target.ClinicalEncounterId != input.ClinicalEncounterId
            || target.RequiresEncounterAssignment || target.ReportFileName is not null || target.Status == "Cancelled")
            return WriteResult.Invalid("Select an unattached pending request belonging to this patient's selected encounter.");
        if (target.CollectedAt.HasValue && target.CollectedAt.Value.Date != source.CollectedAt.Value.Date)
            return WriteResult.Invalid("The actual test date differs. Review and correct the request before assigning this report.");
        if (await db.LabResultItems.AnyAsync(i => i.LabResultId == id, token))
            return WriteResult.Invalid("This held result contains structured items. Review it individually before merging.");
        var bytes = await storage.ReadAsync(source.Id, source.ReportFileName, token);
        await using var stream = new MemoryStream(bytes, writable: false);
        var file = new FormFile(stream, 0, stream.Length, "report", "assigned-lab.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        ReportAttachments.Apply(target, await storage.StoreAsync(target.Id, file, token), DateTime.UtcNow);
        target.CollectedAt ??= source.CollectedAt; target.Status = "Pending Verification"; target.ReleasedAt = null; target.PatientActivityAtUtc = DateTime.UtcNow;
        db.Entry(target).Property("RowVersion").OriginalValue = key.Value.Version;
        db.LabResults.Remove(source);
        await AdminAudit.AddAsync(db, user, "Fulfill request from assigned report", "LabResults", target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.PatientRecordId,
            fields: [nameof(target.ReportFileName), nameof(target.CollectedAt), nameof(target.Status)], cancellationToken: token);
        await AdminAudit.AddAsync(db, user, "Resolve held report", "LabResults", id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.PatientRecordId,
            fields: [nameof(source.RequiresEncounterAssignment)], cancellationToken: token);
        return WriteResult.Success;
    }

    public static async Task<WriteResult> RadiologyAsync(ApplicationDbContext db, IRadiologyReportStorage storage, int id,
        ResultAssignmentInput input, ClaimsPrincipal user, CancellationToken token)
    {
        var version = AdminAudit.Version(input.RowVersion);
        var source = await db.RadiologyStudies.SingleOrDefaultAsync(l => l.Id == id, token);
        if (source is null || version is null || !version.SequenceEqual(db.Entry(source).Property<byte[]>("RowVersion").CurrentValue!)) return WriteResult.Stale;
        if (!source.RequiresEncounterAssignment || source.ReportFileName is null || source.PerformedAt is null)
            return WriteResult.Invalid("This report is no longer waiting for encounter assignment.");
        if (!await EncounterLinks.ValidAsync(db, input.ClinicalEncounterId, source.PatientRecordId, true, token))
            return WriteResult.Invalid("Select an existing encounter belonging to this patient.");
        if (string.IsNullOrWhiteSpace(input.ExistingRequest))
        {
            source.ClinicalEncounterId = input.ClinicalEncounterId; source.RequiresEncounterAssignment = false; source.PatientActivityAtUtc = DateTime.UtcNow;
            db.Entry(source).Property("RowVersion").OriginalValue = version;
            await AdminAudit.AddAsync(db, user, "Assign encounter", "RadiologyStudies", id.ToString(System.Globalization.CultureInfo.InvariantCulture), source.PatientRecordId,
                fields: [nameof(source.ClinicalEncounterId), nameof(source.RequiresEncounterAssignment)], cancellationToken: token);
            return WriteResult.Success;
        }
        var key = PendingRequestKeys.Parse(input.ExistingRequest);
        if (key is null) return WriteResult.Invalid("Reload and select an existing pending request.");
        var target = await db.RadiologyStudies.SingleOrDefaultAsync(l => l.Id == key.Value.Id, token);
        if (target is null || !key.Value.Version.SequenceEqual(db.Entry(target).Property<byte[]>("RowVersion").CurrentValue!)) return WriteResult.Stale;
        if (target.Id == id || target.PatientRecordId != source.PatientRecordId || target.ClinicalEncounterId != input.ClinicalEncounterId
            || target.RequiresEncounterAssignment || target.ReportFileName is not null || target.Status == RadiologyStatus.Cancelled || target.Modality != source.Modality)
            return WriteResult.Invalid("Select an unattached request of the same modality belonging to this patient's selected encounter.");
        if (target.PerformedAt.HasValue && target.PerformedAt.Value.Date != source.PerformedAt.Value.Date)
            return WriteResult.Invalid("The actual study date differs. Review and correct the request before assigning this report.");
        var bytes = await storage.ReadAsync(source.Id, source.ReportFileName, token);
        await using var stream = new MemoryStream(bytes, writable: false);
        var file = new FormFile(stream, 0, stream.Length, "report", "assigned-radiology.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        ReportAttachments.Apply(target, await storage.StoreAsync(target.Id, file, token), DateTime.UtcNow);
        target.PerformedAt ??= source.PerformedAt; target.Status = RadiologyStatus.PendingVerification; target.ReleasedAt = null; target.PatientActivityAtUtc = DateTime.UtcNow;
        db.Entry(target).Property("RowVersion").OriginalValue = key.Value.Version;
        db.RadiologyStudies.Remove(source);
        await AdminAudit.AddAsync(db, user, "Fulfill request from assigned report", "RadiologyStudies", target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.PatientRecordId,
            fields: [nameof(target.ReportFileName), nameof(target.PerformedAt), nameof(target.Status)], cancellationToken: token);
        await AdminAudit.AddAsync(db, user, "Resolve held report", "RadiologyStudies", id.ToString(System.Globalization.CultureInfo.InvariantCulture), target.PatientRecordId,
            fields: [nameof(source.RequiresEncounterAssignment)], cancellationToken: token);
        return WriteResult.Success;
    }
}
