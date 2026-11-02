using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class RedStarReportAccess(ApplicationDbContext db)
{
    // Snapshots are protected by the private-field converter. Identity edits invalidate the mapping
    // even when they originate from an import or a shared-registry editor.
    public static string Fingerprint(RedStarPatient clinic, PatientRecord hospital) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] {
            clinic.Id.ToString(), PatientNames.Clean(clinic.FullName).ToUpperInvariant(), clinic.HospitalNumber?.Trim().ToUpperInvariant(),
            hospital.Id.ToString(), hospital.HospitalNumber?.Trim().ToUpperInvariant(), hospital.DateOfBirth?.ToString("yyyy-MM-dd"),
            PatientNames.Clean(hospital.LastName).ToUpperInvariant(), PatientNames.Clean(hospital.FirstName).ToUpperInvariant(),
            PatientNames.Clean(hospital.MiddleName).ToUpperInvariant(), PatientNames.Clean(hospital.Suffix).ToUpperInvariant()
        }))));

    public static bool MappingValid(RedStarPatient clinic, PatientRecord hospital)
    {
        if (clinic.HospitalPatientRecordId != hospital.Id || clinic.HospitalMappingVerifiedAtUtc is null ||
            string.IsNullOrWhiteSpace(clinic.HospitalMappingFingerprint) || string.IsNullOrWhiteSpace(clinic.HospitalNumber) ||
            !hospital.HasHospitalNumber || hospital.NeedsNameReview || hospital.DateOfBirth is null ||
            !string.Equals(clinic.HospitalNumber.Trim(), hospital.HospitalNumber!.Trim(), StringComparison.OrdinalIgnoreCase) ||
            clinic.PortalUserId is not null && hospital.PortalUserId is not null && clinic.PortalUserId != hospital.PortalUserId) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(clinic.HospitalMappingFingerprint),
            Encoding.UTF8.GetBytes(Fingerprint(clinic, hospital)));
    }

    public async Task<PatientRecord?> MappedPatientAsync(int clinicId, CancellationToken token = default)
    {
        var clinic = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == clinicId, token);
        if (clinic?.HospitalPatientRecordId is not int patientId) return null;
        var hospital = await db.PatientRecords.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, token);
        return hospital is not null && MappingValid(clinic, hospital) ? hospital : null;
    }

    public async Task<IReadOnlyList<RedStarLabReport>> GetOwnedReportsAsync(string userId, CancellationToken token = default)
    {
        var reports = await db.RedStarLabReports.AsNoTracking().Include(r => r.Patient).Include(r => r.Encounter)
            .Include(r => r.RedStarPatient).Where(r => r.Patient.PortalUserId == userId && r.Status != "Withdrawn")
            .ToListAsync(token);
        var visible = reports.Where(r => r.Encounter.PatientRecordId == r.PatientRecordId &&
            MappingValid(r.RedStarPatient, r.Patient)).ToArray();
        if (visible.Length > 0)
            await RedStarAuditTrail.RecordAsync(db, userId, "View owned lab availability", "PrivateLabReports", token);
        return visible;
    }

    public async Task<RedStarLabReport?> GetOwnedReportAsync(string userId, int id, CancellationToken token = default)
    {
        var report = await db.RedStarLabReports.AsNoTracking().Include(r => r.Patient).Include(r => r.Encounter)
            .Include(r => r.RedStarPatient).SingleOrDefaultAsync(r => r.Id == id && r.Patient.PortalUserId == userId && r.Status != "Withdrawn", token);
        return report is not null && report.Encounter.PatientRecordId == report.PatientRecordId &&
            MappingValid(report.RedStarPatient, report.Patient) ? report : null;
    }

    public static bool CanOpenPdf(RedStarLabReport report, bool enabled, DateTime manilaNow) =>
        enabled && report.ReportFileName is not null && report.CollectedAt.HasValue && report.Status == "Available" &&
        report.ReleasedAt.HasValue && report.ReleasedAt >= report.CollectedAt && report.ReleasedAt <= manilaNow;

    public static void Attach(RedStarLabReport report, StoredLabReport? stored)
    {
        report.ReportFileName = stored?.FileName;
        report.ReportOriginalFileNameProtected = stored?.OriginalFileNameProtected;
        report.ReportContentType = stored?.ContentType;
        report.ReportSha256 = stored?.Sha256;
        report.ReportSize = stored?.Size;
        report.ReportUploadedAtUtc = stored is null ? null : DateTime.UtcNow;
        report.PatientActivityAtUtc = DateTime.UtcNow;
    }
}
