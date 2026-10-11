using System.Net;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class RedStarStaffTests(PortalFactory factory)
{
    private sealed record Fixture(int PatientId, int EnrollmentId, int AppointmentId, string UserId, string Email, string Code);
    private Task<HttpClient> Clinic() => StaffAccounts.SignInAsync(factory, "redstar.operations@fixtures.test", "RedStarStaff");
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> operation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
    private async Task<Fixture> Seed(RedStarProgram program = RedStarProgram.ART)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = "redstar.patient." + suffix + "@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        return await Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var patient = new RedStarPatient { PatientCode = "OPS-" + suffix, FullName = "Private synthetic patient " + suffix,
                CodeName = "Synthetic code name", PortalUserId = user.Id, EmailRemindersEnabled = true };
            var enrollment = new RedStarEnrollment { Patient = patient, Program = program, Status = RedStarStatus.Active, Medicines = "Synthetic treatment" };
            var appointment = new RedStarAppointment { Enrollment = enrollment, Kind = RedStarAppointmentKind.Refill,
                DueDate = ClinicalClock.Today.AddDays(7), RemindersVerified = true };
            db.RedStarAppointments.Add(appointment);
            await db.SaveChangesAsync();
            db.RedStarReminders.Add(new RedStarReminder { AppointmentId = appointment.Id, AppointmentRevision = 1, Stage = "SevenDaysBefore",
                Channel = RedStarReminderChannel.Email, PortalUserId = user.Id, ProtectedPayload = [1, 2, 3], CreatedUtc = DateTimeOffset.UtcNow });
            db.RedStarRescheduleRequests.Add(new RedStarRescheduleRequest { AppointmentId = appointment.Id, AppointmentRevision = 1,
                RequestedDate = ClinicalClock.Today.AddDays(9), CreatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return new Fixture(patient.Id, enrollment.Id, appointment.Id, user.Id, email, patient.PatientCode);
        });
    }
    private async Task<Dictionary<string, string>> DispenseFields(Fixture fixture, DateTime? next = null)
    {
        return await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(a => a.Enrollment).SingleAsync(a => a.Id == fixture.AppointmentId);
            var fields = new Dictionary<string, string>
            {
                ["AppointmentVersion"] = Convert.ToBase64String(appointment.RowVersion), ["EnrollmentVersion"] = Convert.ToBase64String(appointment.Enrollment.RowVersion),
                ["DispensedDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"), ["DaysSupplied"] = "30", ["Medicines"] = "Synthetic dispensed medicine"
            };
            if (next.HasValue) { fields["NextRefillDate"] = next.Value.ToString("yyyy-MM-dd"); fields["NextDateVerified"] = "true"; }
            return fields;
        });
    }

    [Fact]
    public async Task Enrollment_creation_is_private_and_portal_link_requires_identity_verification()
    {
        var suffix = Guid.NewGuid().ToString("N"); var code = "CREATE-" + suffix;
        var clinic = await Clinic();
        var created = await clinic.PostFormAsync("/Admin/RedStar/Create", "/Admin/RedStar/Create", new Dictionary<string, string>
        {
            ["PatientCode"] = code, ["CodeName"] = "Synthetic code", ["FullName"] = "Private synthetic creation " + suffix,
            ["Program"] = "ART", ["Status"] = "Active", ["EnrolledDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"), ["Medicines"] = "Synthetic medicine"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var enrollment = await Db(db => db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).SingleAsync(e => e.Patient.PatientCode == code));
        Assert.Equal(RedStarStatus.Active, enrollment.Status);
        Assert.False(await Db(db => db.PatientRecords.AnyAsync(p => p.FullName == enrollment.Patient.FullName)));
        var email = "redstar.link." + suffix + "@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        var fields = new Dictionary<string, string> { ["Email"] = email, ["PatientVersion"] = Convert.ToBase64String(enrollment.Patient.RowVersion) };
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + enrollment.Id, "/Admin/RedStar/Link/" + enrollment.PatientId, fields);
        Assert.Null(await Db(db => db.RedStarPatients.Where(p => p.Id == enrollment.PatientId).Select(p => p.PortalUserId).SingleAsync()));
        fields["IdentityVerified"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + enrollment.Id, "/Admin/RedStar/Link/" + enrollment.PatientId, fields)).StatusCode);
        var linked = await Db(db => db.RedStarPatients.AsNoTracking().SingleAsync(p => p.Id == enrollment.PatientId));
        Assert.Equal(await Db(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()), linked.PortalUserId);
        Assert.False(linked.EmailRemindersEnabled);
        Assert.True(await Db(db => db.RedStarAudits.AnyAsync(a => a.Subject == "Patient/" + linked.Id && a.Action == "Verify portal link")));
        Assert.False(await Db(db => db.AdminAuditLogs.AnyAsync(a => a.Entity == "RedStar")));
    }

    [Fact]
    public async Task Unlink_cancels_pending_delivery_requests_and_old_account_binding()
    {
        var item = await Seed(); var clinic = await Clinic();
        var version = await Db(async db => Convert.ToBase64String(await db.RedStarPatients.Where(p => p.Id == item.PatientId).Select(p => p.RowVersion).SingleAsync()));
        var response = await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Unlink/" + item.PatientId,
            new Dictionary<string, string> { ["PatientVersion"] = version, ["IdentityVerified"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var patient = await Db(db => db.RedStarPatients.AsNoTracking().SingleAsync(p => p.Id == item.PatientId));
        Assert.Null(patient.PortalUserId); Assert.False(patient.EmailRemindersEnabled);
        var appointment = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.Equal(2, appointment.Revision); Assert.Equal(RedStarAppointmentState.Scheduled, appointment.State);
        var delivery = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        Assert.Equal(RedStarDeliveryState.Cancelled, delivery.State); Assert.Null(delivery.ProtectedPayload);
        Assert.Equal(RedStarRequestState.Declined, await Db(db => db.RedStarRescheduleRequests.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
    }

    [Fact]
    public async Task Dispensing_is_unique_and_supply_estimate_does_not_choose_the_next_visit()
    {
        var item = await Seed(); var clinic = await Clinic(); var next = ClinicalClock.Today.AddDays(14);
        var fields = await DispenseFields(item, next);
        var response = await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Dispense/" + item.AppointmentId, fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var dispense = await Db(db => db.RedStarDispenses.AsNoTracking().SingleAsync(d => d.AppointmentId == item.AppointmentId));
        Assert.Equal(30, dispense.DaysSupplied); Assert.Equal(ClinicalClock.Today.AddDays(30), dispense.EstimatedSupplyEnd);
        Assert.Equal("Synthetic dispensed medicine", await Db(db => db.RedStarEnrollments.Where(e => e.Id == item.EnrollmentId).Select(e => e.Medicines).SingleAsync()));
        var nextAppointment = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.EnrollmentId == item.EnrollmentId && a.State == RedStarAppointmentState.Scheduled));
        Assert.Equal(next, nextAppointment.DueDate); Assert.True(nextAppointment.RemindersVerified);
        Assert.Equal(RedStarAppointmentState.PickedUp, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.State).SingleAsync()));
        Assert.Equal(RedStarDeliveryState.Cancelled, await Db(db => db.RedStarReminders.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/Dispense/" + item.AppointmentId, fields);
        Assert.Equal(1, await Db(db => db.RedStarDispenses.CountAsync(d => d.AppointmentId == item.AppointmentId)));
        Assert.Equal(2, await Db(db => db.RedStarAppointments.CountAsync(a => a.EnrollmentId == item.EnrollmentId)));
        Assert.Contains("This record changed", await clinic.GetStringAsync("/Admin/RedStar/Details/" + item.EnrollmentId));
    }

    [Theory]
    [InlineData(RedStarProgram.PEP)]
    [InlineData(RedStarProgram.NewbornProphylaxis)]
    public async Task One_course_dispensing_never_creates_an_automatic_refill(RedStarProgram program)
    {
        var item = await Seed(program); var clinic = await Clinic();
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId,
            "/Admin/RedStar/Dispense/" + item.AppointmentId, await DispenseFields(item))).StatusCode);
        Assert.False(await Db(db => db.RedStarAppointments.AnyAsync(a => a.EnrollmentId == item.EnrollmentId && a.State == RedStarAppointmentState.Scheduled)));
    }

    [Fact]
    public async Task Concurrent_pickups_commit_a_single_history_record()
    {
        var item = await Seed(); var clinic = await Clinic(); var fields = await DispenseFields(item);
        fields["__RequestVerificationToken"] = StaffAccounts.Token(await clinic.GetStringAsync("/Admin/RedStar/Details/" + item.EnrollmentId));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => clinic.PostAsync("/Admin/RedStar/Dispense/" + item.AppointmentId, new FormUrlEncodedContent(fields))));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode));
        Assert.Equal(1, await Db(db => db.RedStarDispenses.CountAsync(d => d.AppointmentId == item.AppointmentId)));
    }

    [Fact]
    public async Task Staff_can_verify_an_existing_overdue_date_without_moving_it()
    {
        var item = await Seed(); var clinic = await Clinic(); var overdue = ClinicalClock.Today.AddDays(-14);
        await Db(async db => { var appointment = await db.RedStarAppointments.SingleAsync(a => a.Id == item.AppointmentId);
            appointment.DueDate = overdue; appointment.RemindersVerified = false; await db.SaveChangesAsync(); return true; });
        var fields = await Db(async db =>
        {
            var appointment = await db.RedStarAppointments.Include(a => a.Enrollment).SingleAsync(a => a.Id == item.AppointmentId);
            return new Dictionary<string, string> { ["AppointmentVersion"] = Convert.ToBase64String(appointment.RowVersion),
                ["EnrollmentVersion"] = Convert.ToBase64String(appointment.Enrollment.RowVersion), ["Kind"] = "Refill",
                ["DueDate"] = overdue.ToString("yyyy-MM-dd"), ["RemindersVerified"] = "true" };
        });
        Assert.Equal(HttpStatusCode.Redirect, (await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId,
            "/Admin/RedStar/UpdateAppointment/" + item.AppointmentId, fields)).StatusCode);
        var verified = await Db(db => db.RedStarAppointments.AsNoTracking().SingleAsync(a => a.Id == item.AppointmentId));
        Assert.Equal(overdue, verified.DueDate); Assert.True(verified.RemindersVerified); Assert.Equal(2, verified.Revision);
        fields["AppointmentVersion"] = Convert.ToBase64String(verified.RowVersion); fields["DueDate"] = overdue.AddDays(1).ToString("yyyy-MM-dd");
        await clinic.PostFormAsync("/Admin/RedStar/Details/" + item.EnrollmentId, "/Admin/RedStar/UpdateAppointment/" + item.AppointmentId, fields);
        Assert.Equal(overdue, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.DueDate).SingleAsync()));
    }

    [Fact]
    public async Task Closing_enrollment_cancels_schedules_and_reminders_in_one_transaction()
    {
        var item = await Seed(); var clinic = await Clinic();
        var enrollment = await Db(db => db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).SingleAsync(e => e.Id == item.EnrollmentId));
        var response = await clinic.PostFormAsync("/Admin/RedStar/Edit/" + item.EnrollmentId, "/Admin/RedStar/Edit/" + item.EnrollmentId,
            new Dictionary<string, string>
            {
                ["PatientCode"] = item.Code, ["CodeName"] = enrollment.Patient.CodeName, ["FullName"] = enrollment.Patient.FullName,
                ["Program"] = "ART", ["Status"] = "TransferredOut", ["StatusDate"] = ClinicalClock.Today.ToString("yyyy-MM-dd"),
                ["ReceivingHub"] = "Synthetic receiving clinic", ["Medicines"] = enrollment.Medicines,
                ["PatientVersion"] = Convert.ToBase64String(enrollment.Patient.RowVersion), ["EnrollmentVersion"] = Convert.ToBase64String(enrollment.RowVersion)
            });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RedStarStatus.TransferredOut, await Db(db => db.RedStarEnrollments.Where(e => e.Id == item.EnrollmentId).Select(e => e.Status).SingleAsync()));
        Assert.Equal(RedStarAppointmentState.Cancelled, await Db(db => db.RedStarAppointments.Where(a => a.Id == item.AppointmentId).Select(a => a.State).SingleAsync()));
        Assert.Equal(RedStarRequestState.Declined, await Db(db => db.RedStarRescheduleRequests.Where(r => r.AppointmentId == item.AppointmentId).Select(r => r.State).SingleAsync()));
        var reminder = await Db(db => db.RedStarReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == item.AppointmentId));
        Assert.Equal(RedStarDeliveryState.Cancelled, reminder.State); Assert.Null(reminder.ProtectedPayload);
    }

    [Fact]
    public async Task Live_role_revocation_and_lockout_deny_clinic_access()
    {
        var email = "redstar.revoked." + Guid.NewGuid().ToString("N") + "@fixtures.test";
        var client = await StaffAccounts.SignInAsync(factory, email, "RedStarStaff");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/RedStar")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await users.FindByEmailAsync(email))!;
        Assert.True((await users.RemoveFromRoleAsync(user, "RedStarStaff")).Succeeded);
        var denied = await client.GetAsync("/Admin/RedStar");
        Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        Assert.True((await users.AddToRoleAsync(user, "RedStarStaff")).Succeeded);
        await users.SetLockoutEnabledAsync(user, true); await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1));
        denied = await client.GetAsync("/Admin/RedStar"); Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }
}
