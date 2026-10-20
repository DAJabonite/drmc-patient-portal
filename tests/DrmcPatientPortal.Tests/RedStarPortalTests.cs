using System.Net;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class RedStarPortalTests(PortalFactory factory)
{
    private sealed record Fixture(string UserId, string Email, int PatientId, int EnrollmentId, int AppointmentId, string Code, string Name);
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
    private async Task<Fixture> Seed()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = "private." + suffix + "@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        return await Db(async db =>
        {
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var patient = new RedStarPatient { PatientCode = "RS-" + suffix, CodeName = "Synthetic code name", FullName = "PRIVATE-IDENTITY-" + suffix, PortalUserId = userId };
            var enrollment = new RedStarEnrollment { Patient = patient, Status = RedStarStatus.Active, Program = RedStarProgram.ART, Medicines = "PRIVATE-MEDICINE-" + suffix };
            var appointment = new RedStarAppointment { Enrollment = enrollment, DueDate = ClinicalClock.Today.AddDays(7), Kind = RedStarAppointmentKind.Refill, RemindersVerified = true };
            db.RedStarAppointments.Add(appointment); await db.SaveChangesAsync();
            return new Fixture(userId, email, patient.Id, enrollment.Id, appointment.Id, patient.PatientCode, patient.FullName);
        });
    }
    private async Task<HttpClient> Patient(Fixture item)
    {
        var client = factory.CreatePortalClient(); await client.SignInSuccessfullyAsync(item.Email, PortalFactory.Password); return client;
    }
    private Task<HttpClient> Clinic() => StaffAccounts.SignInAsync(factory, "redstar.staff@fixtures.test", "RedStarStaff");
    private Task<string> Version(int id) => Db(async db => Convert.ToBase64String(await db.RedStarAppointments.Where(a => a.Id == id).Select(a => a.RowVersion).SingleAsync()));

    [Fact]
    public async Task All_clinic_surfaces_deny_non_clinic_roles_and_no_mfa_staff()
    {
        var outsider = factory.CreatePortalClient(); await outsider.SignInSuccessfullyAsync(PortalFactory.EmptyEmail, PortalFactory.Password);
        var clients = new[] { await StaffAccounts.AdminAsync(factory), await StaffAccounts.LabAsync(factory), await StaffAccounts.RadiologyAsync(factory), await StaffAccounts.PatientServicesAsync(factory), outsider };
        foreach (var client in clients)
        {
            foreach (var path in new[] { "/Admin/RedStar", "/Admin/RedStar/Create", "/Admin/RedStar/Audit", "/Admin/RedStar/Deliveries", "/Admin/RedStarImports" })
            {
                var denied = await client.GetAsync(path);
                Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, path + ": " + denied.StatusCode);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/Admin/RedStarImports/Export", new FormUrlEncodedContent([]))).StatusCode);
        }
        var anonymous = factory.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/Admin/RedStar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/Patient/RedStar")).StatusCode);
        var email = "nomfa.redstar@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false, "RedStarStaff");
        var noMfa = factory.CreatePortalClient(); await noMfa.SignInSuccessfullyAsync(email, PortalFactory.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await noMfa.GetAsync("/Admin/RedStar")).StatusCode);
    }

    [Fact]
    public async Task Registry_lists_use_codes_and_general_admin_has_no_private_records_or_counts()
    {
        var item = await Seed(); var clinic = await Clinic();
        var list = await clinic.GetAsync("/Admin/RedStar?search=" + item.Code);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.True(list.Headers.CacheControl?.NoStore);
        var html = await list.Content.ReadAsStringAsync(); Assert.Contains(item.Code, html); Assert.DoesNotContain(item.Name, html);
        Assert.Contains("href=\"/Admin/RedStar?worklist=week\"", html);
        Assert.Contains("action=\"/Admin/RedStar\"", html);
        Assert.DoesNotContain("href=\"/Patient/RedStar", html);
        var detail = await clinic.GetAsync("/Admin/RedStar/Details/" + item.EnrollmentId);
        Assert.True(detail.IsSuccessStatusCode, await detail.Content.ReadAsStringAsync());
        Assert.Contains(item.Name, await detail.Content.ReadAsStringAsync());
        var admin = await StaffAccounts.AdminAsync(factory);
        foreach (var path in new[] { "/Admin", "/Admin/Patients", "/Admin/Audit", "/Admin/Imports", "/Admin/Prescriptions" })
        {
            var general = await admin.GetStringAsync(path); Assert.DoesNotContain(item.Code, general); Assert.DoesNotContain(item.Name, general);
            Assert.DoesNotContain("href=\"/Admin/RedStar\"", general);
        }
    }

    [Fact]
    public async Task Patient_and_medications_share_schedule_and_general_link_cannot_transfer_access()
    {
        var a = await Seed(); var b = await Seed(); var clientA = await Patient(a); var clientB = await Patient(b);
        var schedule = await clientA.GetAsync("/Patient/RedStar"); Assert.Equal(HttpStatusCode.OK, schedule.StatusCode); Assert.True(schedule.Headers.CacheControl?.NoStore);
        var page = await schedule.Content.ReadAsStringAsync(); var meds = await clientA.GetStringAsync("/Patient/Medications");
        var date = ClinicalClock.Today.AddDays(7).ToString("MMMM d, yyyy");
        Assert.Contains(date, page); Assert.Contains(date, meds); Assert.Contains("View Red Star schedule", meds);
        Assert.DoesNotContain(b.Code, page); Assert.DoesNotContain(b.Name, page);
        await Db(async db => { db.PatientRecords.Add(new PatientRecord { FirstName = a.Name, LastName = "Fixture", PortalUserId = b.UserId }); await db.SaveChangesAsync(); return true; });
        var bPage = await clientB.GetStringAsync("/Patient/RedStar"); Assert.DoesNotContain("PRIVATE-MEDICINE-" + a.Code[3..], bPage);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/Confirm/" + a.AppointmentId,
            new Dictionary<string, string> { ["rowVersion"] = await Version(a.AppointmentId) })).StatusCode);
    }

    [Fact]
    public async Task Confirmation_is_not_dispensing_and_requests_wait_for_staff_approval_with_stale_write_rejection()
    {
        var item = await Seed(); var client = await Patient(item); var oldVersion = await Version(item.AppointmentId);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/Confirm/" + item.AppointmentId,
            new Dictionary<string, string> { ["rowVersion"] = oldVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/Confirm/" + item.AppointmentId,
            new Dictionary<string, string> { ["rowVersion"] = oldVersion })).StatusCode);
        var appointment = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.NotNull(appointment.ConfirmedUtc); Assert.Equal(RedStarAppointmentState.Scheduled, appointment.State);
        Assert.False(await Db(db => db.RedStarDispenses.AnyAsync(d => d.AppointmentId == item.AppointmentId)));
        var preferred = ClinicalClock.Today.AddDays(5);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/RequestReschedule/" + item.AppointmentId,
            new Dictionary<string, string> { ["RowVersion"] = await Version(item.AppointmentId), ["RequestedDate"] = preferred.ToString("yyyy-MM-dd"), ["Reason"] = "Synthetic request" })).StatusCode);
        Assert.Equal(appointment.DueDate, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.DueDate).SingleAsync()));
        var request = await Db(db => db.RedStarRescheduleRequests.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        var clinic = await Clinic();
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/DecideRequest/" + request.Id,
            new Dictionary<string, string> { ["Input.RequestVersion"] = Convert.ToBase64String(request.RowVersion), ["Input.AppointmentVersion"] = await Version(item.AppointmentId),
                ["Input.AppointmentRevision"] = request.AppointmentRevision.ToString(), ["Input.Approve"] = "true", ["Input.ApprovedDate"] = preferred.ToString("yyyy-MM-dd") })).StatusCode);
        var revised = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.Equal(preferred, revised.DueDate); Assert.Null(revised.ConfirmedUtc); Assert.Equal(2, revised.Revision);
    }

    [Fact]
    public async Task Email_opt_in_is_private_and_revoking_it_cancels_queued_payloads()
    {
        var item = await Seed(); var client = await Patient(item);
        var version = await Db(async db => Convert.ToBase64String(await db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.RowVersion).SingleAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/Preferences",
            new Dictionary<string, string> { ["rowVersion"] = version, ["emailEnabled"] = "true" })).StatusCode);
        await Db(async db => { db.RedStarReminders.Add(new RedStarReminder { AppointmentId = item.AppointmentId, AppointmentRevision = 1,
            Stage = "SevenDaysBefore", Channel = RedStarReminderChannel.Email, PortalUserId = item.UserId, ProtectedPayload = [1,2], CreatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(); return true; });
        version = await Db(async db => Convert.ToBase64String(await db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.RowVersion).SingleAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/Patient/RedStar", "/Patient/RedStar/Preferences", new Dictionary<string, string> { ["rowVersion"] = version })).StatusCode);
        var reminder = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        Assert.Equal(RedStarDeliveryState.Cancelled, reminder.State); Assert.Null(reminder.ProtectedPayload);
    }

    [Fact]
    public async Task Private_audit_failure_blocks_clinic_and_patient_disclosure()
    {
        var item = await Seed();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(new RejectAudit()))));
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await client.SignInSuccessfullyAsync(item.Email, PortalFactory.Password);
        var response = await client.GetAsync("/Patient/RedStar"); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/ServiceUnavailable", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(item.Name, await response.Content.ReadAsStringAsync());
    }
    private sealed class RejectAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RedStarAudit>().Any(e => e.State == EntityState.Added) == true) throw new IOException("Synthetic audit storage failure.");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Live_lockout_revokes_private_reads_and_actions_and_empty_refill_card_is_safe()
    {
        var item = await Seed(); var client = await Patient(item);
        await Db(async db => { var appointment = await db.RedStarAppointments.SingleAsync(a => a.Id == item.AppointmentId);
            appointment.State = RedStarAppointmentState.Cancelled; await db.SaveChangesAsync(); return true; });
        var meds = await client.GetAsync("/Patient/Medications");
        Assert.Equal(HttpStatusCode.OK, meds.StatusCode);
        Assert.Contains("No refill date has been scheduled", await meds.Content.ReadAsStringAsync());
        var page = await client.GetStringAsync("/Patient/RedStar");
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        await Db(async db => { var user = await db.Users.SingleAsync(u => u.Id == item.UserId);
            user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); await db.SaveChangesAsync(); return true; });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Patient/RedStar")).StatusCode);
        var denied = await client.PostAsync("/Patient/RedStar/Preferences", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = token, ["emailEnabled"] = "true",
            ["rowVersion"] = await Db(async db => Convert.ToBase64String(await db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.RowVersion).SingleAsync())) }));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.False(await Db(db => db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.EmailRemindersEnabled).SingleAsync()));
    }

    [Fact]
    public async Task Historical_registration_labels_are_neutral_in_shared_views()
    {
        var unique = Guid.NewGuid().ToString("N");
        await Db(async db => { db.PatientRegistrationCodes.Add(new PatientRegistrationCode { PatientRecord = new PatientRecord { FirstName = "Synthetic legacy", LastName = unique },
            IssuingPoint = "Red Star Clinic", CodeHint = "ABCDE", CodeHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(unique)),
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) }); await db.SaveChangesAsync(); return true; });
        var html = await (await StaffAccounts.PatientServicesAsync(factory)).GetStringAsync("/Admin/RegistrationCodes?search=" + unique);
        Assert.Contains("Clinic registration desk", html); Assert.DoesNotContain("Red Star", html);
    }
}
