using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

// The actor is supplied only by an MFA-authenticated request or the import worker's
// freshly verified approving Admin. Roles are always read again from the database.
public sealed record PrescriptionActor(string UserId, bool MfaAuthenticated)
{
    public static PrescriptionActor? FromPrincipal(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true && principal.FindFirstValue(ClaimTypes.NameIdentifier) is string id
            ? new(id, principal.HasClaim("amr", "mfa")) : null;
}

public sealed class PrescriptionAccess(PrescriptionActor? actor)
{
    public bool IsAdmin { get; private set; }
    public bool IsRedStar { get; private set; }
    private IReadOnlyList<RedStarEnrollment> enrollments = [];
    private int[] enrollmentIds = [], patientIds = [];

    public async Task<bool> InitializeAsync(ApplicationDbContext db, CancellationToken token)
    {
        IsAdmin = IsRedStar = false;
        enrollments = []; enrollmentIds = []; patientIds = [];
        if (actor?.MfaAuthenticated != true) return false;
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor.UserId, token);
        if (account is null || !account.EmailConfirmed || !account.TwoFactorEnabled || account.LockoutEnabled && account.LockoutEnd > DateTimeOffset.UtcNow) return false;
        var roles = await (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
            where membership.UserId == actor.UserId select role.Name).ToListAsync(token);
        IsAdmin = roles.Contains(StaffRoles.Admin); IsRedStar = roles.Contains(StaffRoles.RedStar);
        if (IsAdmin) return true;
        if (!IsRedStar) return false;
        enrollments = await PrescriptionQueries.ValidEnrollmentsAsync(db, token);
        enrollmentIds = enrollments.Select(e => e.Id).ToArray();
        patientIds = enrollments.Select(e => e.Patient.HospitalPatientRecordId!.Value).Distinct().ToArray();
        return true;
    }

    public async Task<IReadOnlyList<RedStarEnrollment>> EnrollmentsForAsync(ApplicationDbContext db, int patientId, CancellationToken token) =>
        IsAdmin ? await PrescriptionQueries.ValidEnrollmentsAsync(db, token, patientId) :
        enrollments.Where(e => e.Patient.HospitalPatientRecordId == patientId).ToArray();

    public IQueryable<Prescription> Query(ApplicationDbContext db) => IsAdmin ? db.Prescriptions :
        db.Prescriptions.Where(p => IsRedStar && p.IsRedStar && p.RedStarEnrollmentId.HasValue &&
            EF.Parameter(enrollmentIds).Contains(p.RedStarEnrollmentId.Value) && p.RedStarEnrollment!.Patient.HospitalPatientRecordId == p.PatientRecordId);
    public IQueryable<PatientRecord> Patients(ApplicationDbContext db) => IsAdmin ? db.PatientRecords :
        db.PatientRecords.Where(p => IsRedStar && EF.Parameter(patientIds).Contains(p.Id));
}
