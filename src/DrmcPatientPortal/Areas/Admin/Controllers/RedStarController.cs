using System.Globalization;
using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = AdminPolicies.RedStar)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RedStarController(ApplicationDbContext db, AdminWrites writes) : Controller
{
    private const int PageSize = 25;
    private static readonly string[] Worklists = ["all", "today", "week", "overdue", "followup", "verification", "requests", "expired"];
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("A clinic actor is required.");

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string worklist = "all", RedStarProgram? program = null,
        RedStarStatus? status = null, int page = 1, string? month = null, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (!ModelState.IsValid || search?.Length > 120 || !Worklists.Contains(worklist) || program.HasValue && !Enum.IsDefined(program.Value) ||
            status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        var today = ClinicalClock.Today;
        month ??= today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart) ||
            monthStart.Year is < 1900 or > 2200) return BadRequest();
        var monthEnd = monthStart.AddMonths(1);
        var end = today.AddDays(RedStarReminderService.ReminderWindowDays);
        var all = db.RedStarEnrollments.AsNoTracking();
        var refills = OutstandingRefills();
        var counts = new Dictionary<string, int>
        {
            ["today"] = await all.CountAsync(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate == today), cancellationToken),
            ["week"] = await all.CountAsync(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate >= today && a.DueDate <= end), cancellationToken),
            ["overdue"] = await all.CountAsync(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate < today), cancellationToken),
            ["followup"] = await all.CountAsync(e => e.Program == RedStarProgram.ART && refills.Any(a => a.EnrollmentId == e.Id && a.DueDate.AddMonths(1) <= today), cancellationToken),
            ["verification"] = await all.CountAsync(e => e.Status == RedStarStatus.AwaitingVerification || e.Appointments.Any(a => a.State == RedStarAppointmentState.Scheduled && !a.RemindersVerified), cancellationToken),
            ["requests"] = await all.CountAsync(e => db.RedStarRescheduleRequests.Any(r => r.Appointment.EnrollmentId == e.Id && r.State == RedStarRequestState.Pending), cancellationToken),
            ["living"] = await all.CountAsync(e => e.Program == RedStarProgram.ART && e.Status == RedStarStatus.Active, cancellationToken),
            ["expired"] = await all.CountAsync(e => e.Status == RedStarStatus.Deceased && e.StatusDate >= monthStart && e.StatusDate < monthEnd, cancellationToken)
        };
        var query = worklist switch
        {
            "today" => all.Where(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate == today)),
            "week" => all.Where(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate >= today && a.DueDate <= end)),
            "overdue" => all.Where(e => refills.Any(a => a.EnrollmentId == e.Id && a.DueDate < today)),
            "followup" => all.Where(e => e.Program == RedStarProgram.ART && refills.Any(a => a.EnrollmentId == e.Id && a.DueDate.AddMonths(1) <= today)),
            "verification" => all.Where(e => e.Status == RedStarStatus.AwaitingVerification || e.Appointments.Any(a => a.State == RedStarAppointmentState.Scheduled && !a.RemindersVerified)),
            "requests" => all.Where(e => db.RedStarRescheduleRequests.Any(r => r.Appointment.EnrollmentId == e.Id && r.State == RedStarRequestState.Pending)),
            "expired" => all.Where(e => e.Status == RedStarStatus.Deceased && e.StatusDate >= monthStart && e.StatusDate < monthEnd),
            _ => all
        };
        if (program.HasValue) query = query.Where(e => e.Program == program);
        if (status.HasValue) query = query.Where(e => e.Status == status);
        // Ciphertext cannot be searched or sorted by SQL. Project only the registry
        // summary, then apply the existing private search/order before pagination.
        var summaries = await query
            .Select(e => new RedStarRegistryRow(e.Id, e.PatientId, e.Patient.PatientCode, e.Patient.CodeName, e.Program, e.Status,
                refills.Where(a => a.EnrollmentId == e.Id).OrderBy(a => a.DueDate).ThenBy(a => a.Id)
                    .Select(a => (DateTime?)a.DueDate).FirstOrDefault(),
                db.RedStarRescheduleRequests.Count(r => r.Appointment.EnrollmentId == e.Id && r.State == RedStarRequestState.Pending),
                e.Program == RedStarProgram.ART && refills.Any(a => a.EnrollmentId == e.Id && a.DueDate.AddMonths(1) <= today),
                refills.Where(a => a.EnrollmentId == e.Id).OrderBy(a => a.DueDate).ThenBy(a => a.Id)
                    .Select(a => (bool?)a.RemindersVerified).FirstOrDefault(), e.StatusDate))
            .ToListAsync(cancellationToken);
        var matching = summaries.Where(e => string.IsNullOrEmpty(search) ||
            RedStarPhiProtection.Contains(e.PatientCode, search) || RedStarPhiProtection.Contains(e.CodeName, search)).ToList();
        var total = matching.Count;
        page = Page(page, total);
        var rows = matching.OrderBy(e => e.PatientCode, RedStarPhiProtection.RegistryOrder).ThenBy(e => e.EnrollmentId)
            .Skip((page - 1) * PageSize).Take(PageSize).ToList();
        await RedStarAuditTrail.RecordAsync(db, Actor, "View registry", "Registry", cancellationToken);
        return View(new RedStarRegistryPage(rows, search, worklist, program, status, page, total, counts, today, month));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int patientId = 0, CancellationToken cancellationToken = default)
    {
        if (patientId == 0) return View(new RedStarEnrollmentInput { EnrolledDate = ClinicalClock.Today });
        var patient = await db.RedStarPatients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, Actor, "View enrollment form", $"Patient/{patientId}", cancellationToken);
        ViewData["PatientId"] = patientId;
        return View(new RedStarEnrollmentInput
        {
            PatientCode = patient.PatientCode, CodeName = patient.CodeName, FullName = patient.FullName,
            HospitalNumber = patient.HospitalNumber, Address = patient.Address, ContactNumber = patient.ContactNumber,
            PatientVersion = Version(patient.RowVersion), EnrolledDate = ClinicalClock.Today
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(RedStarEnrollmentInput input, int patientId = 0, CancellationToken cancellationToken = default)
    {
        ValidateEnrollment(input);
        if (patientId != 0 && AdminAudit.Version(input.PatientVersion) is null) ModelState.AddModelError("", "Reload the patient before adding an enrollment.");
        if (!ModelState.IsValid) { ViewData["PatientId"] = patientId; return View(input); }
        var enrollmentId = 0;
        var result = await writes.RunAsync(async database =>
        {
            RedStarPatient? patient;
            if (patientId == 0)
            {
                var code = input.PatientCode.Trim();
                var codeIndex = database.Phi.PatientCodeIndex(code);
                if (await database.RedStarPatients.AnyAsync(p => p.PatientCodeBlindIndex == codeIndex, cancellationToken))
                    return WriteResult.Invalid("That patient code already exists. Open its record to add another program.");
                patient = new RedStarPatient(); ApplyPatient(input, patient);
                database.RedStarPatients.Add(patient);
            }
            else
            {
                patient = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
                if (patient is null || !Matches(patient.RowVersion, input.PatientVersion)) return WriteResult.Stale;
                if (await database.RedStarEnrollments.AnyAsync(e => e.PatientId == patientId && e.Program == input.Program, cancellationToken))
                    return WriteResult.Invalid("This patient already has an enrollment in that program. Update its existing record.");
            }
            var enrollment = new RedStarEnrollment { Patient = patient };
            ApplyEnrollment(input, enrollment);
            database.RedStarEnrollments.Add(enrollment);
            await database.SaveChangesAsync(cancellationToken);
            enrollmentId = enrollment.Id;
            await RedStarAuditTrail.RecordAsync(database, Actor, "Create enrollment", $"Enrollment/{enrollment.Id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) return RedirectToAction(nameof(Details), new { id = enrollmentId });
        if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError("", result.Error!); ViewData["PatientId"] = patientId;
        return View(input);
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, int appointmentPage = 1, int historyPage = 1, int requestPage = 1,
        CancellationToken cancellationToken = default)
    {
        var enrollment = await db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (enrollment is null) return NotFound();
        ViewData["LinkedAccountEmail"] = enrollment.Patient.PortalUserId is null ? null :
            await db.Users.Where(u => u.Id == enrollment.Patient.PortalUserId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
        var appointments = db.RedStarAppointments.AsNoTracking().Where(a => a.EnrollmentId == id);
        var dispenses = db.RedStarDispenses.AsNoTracking().Where(d => d.EnrollmentId == id);
        var requests = db.RedStarRescheduleRequests.AsNoTracking().Include(r => r.Appointment).Where(r => r.Appointment.EnrollmentId == id);
        var appointmentTotal = await appointments.CountAsync(cancellationToken);
        var historyTotal = await dispenses.CountAsync(cancellationToken);
        var requestTotal = await requests.CountAsync(cancellationToken);
        appointmentPage = Page(appointmentPage, appointmentTotal); historyPage = Page(historyPage, historyTotal); requestPage = Page(requestPage, requestTotal);
        var model = new RedStarDetailsPage(enrollment,
            await appointments.OrderBy(a => a.State != RedStarAppointmentState.Scheduled).ThenBy(a => a.DueDate).ThenBy(a => a.Id)
                .Skip((appointmentPage - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken),
            await dispenses.OrderByDescending(d => d.DispensedDate).ThenByDescending(d => d.Id)
                .Skip((historyPage - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken),
            await requests.OrderBy(r => r.State != RedStarRequestState.Pending).ThenByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id)
                .Skip((requestPage - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken),
            appointmentPage, appointmentTotal, historyPage, historyTotal, requestPage, requestTotal);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View enrollment", $"Enrollment/{id}", cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var enrollment = await db.RedStarEnrollments.AsNoTracking().Include(e => e.Patient).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (enrollment is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, Actor, "View edit form", $"Enrollment/{id}", cancellationToken);
        ViewData["EnrollmentId"] = id;
        return View(new RedStarEnrollmentInput
        {
            PatientCode = enrollment.Patient.PatientCode, CodeName = enrollment.Patient.CodeName, FullName = enrollment.Patient.FullName,
            HospitalNumber = enrollment.Patient.HospitalNumber, ContactNumber = enrollment.Patient.ContactNumber, Address = enrollment.Patient.Address,
            Program = enrollment.Program, Status = enrollment.Status, EnrolledDate = enrollment.EnrolledDate, StartDate = enrollment.StartDate,
            StatusDate = enrollment.StatusDate, ReceivingHub = enrollment.ReceivingHub, Medicines = enrollment.Medicines,
            PatientVersion = Version(enrollment.Patient.RowVersion), EnrollmentVersion = Version(enrollment.RowVersion)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, RedStarEnrollmentInput input, CancellationToken cancellationToken)
    {
        ValidateEnrollment(input);
        if (AdminAudit.Version(input.PatientVersion) is null || AdminAudit.Version(input.EnrollmentVersion) is null)
            ModelState.AddModelError("", "Reload the record before editing it.");
        if (!ModelState.IsValid) { ViewData["EnrollmentId"] = id; return View(input); }
        var result = await writes.RunAsync(async database =>
        {
            var enrollment = await database.RedStarEnrollments.Include(e => e.Patient).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (enrollment is null || !Matches(enrollment.RowVersion, input.EnrollmentVersion) || !Matches(enrollment.Patient.RowVersion, input.PatientVersion)) return WriteResult.Stale;
            var code = input.PatientCode.Trim();
            var codeIndex = database.Phi.PatientCodeIndex(code);
            if (await database.RedStarPatients.AnyAsync(p => p.Id != enrollment.PatientId && p.PatientCodeBlindIndex == codeIndex, cancellationToken))
                return WriteResult.Invalid("That patient code is already assigned.");
            if (await database.RedStarEnrollments.AnyAsync(e => e.Id != id && e.PatientId == enrollment.PatientId && e.Program == input.Program, cancellationToken))
                return WriteResult.Invalid("This patient already has an enrollment in that program. Update its existing record.");
            var closed = input.Status is RedStarStatus.TransferredOut or RedStarStatus.Discontinued or RedStarStatus.Completed or RedStarStatus.Deceased;
            var needsVerification = input.Program != enrollment.Program || enrollment.Status == RedStarStatus.Active && input.Status == RedStarStatus.AwaitingVerification;
            if (closed || needsVerification)
            {
                var careDates = database.RedStarAppointments.Where(a => a.EnrollmentId == id);
                await InvalidateAppointments(database, careDates, closed, cancellationToken);
                if (needsVerification)
                    foreach (var appointment in await careDates.Where(a => a.State == RedStarAppointmentState.Scheduled).ToListAsync(cancellationToken))
                        appointment.RemindersVerified = false;
            }
            ApplyPatient(input, enrollment.Patient); ApplyEnrollment(input, enrollment);
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Edit enrollment", $"Enrollment/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        if (result.Succeeded) return Success(id, "Enrollment updated.");
        if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError("", result.Error!); ViewData["EnrollmentId"] = id; return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Link([FromRoute] int id, RedStarLinkInput input, CancellationToken cancellationToken)
    {
        if (!input.IdentityVerified) ModelState.AddModelError("", "Verify the patient's identity and ownership of the portal account before linking.");
        if (!ModelState.IsValid) return await PatientResult(id, WriteResult.Invalid(ValidationMessage()), "", cancellationToken);
        var result = await writes.RunAsync(async database =>
        {
            var patient = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (patient is null || !Matches(patient.RowVersion, input.PatientVersion)) return WriteResult.Stale;
            if (patient.PortalUserId is not null) return WriteResult.Invalid("Unlink the existing account before linking another account.");
            var email = input.Email.Trim();
            var accounts = await database.Users.Where(u => u.EmailConfirmed && u.Email != null &&
                EF.Functions.Collate(u.Email, "Latin1_General_100_CI_AS") == email).Take(2).ToListAsync(cancellationToken);
            if (accounts.Count != 1) return WriteResult.Invalid("Select one existing, email-confirmed portal account.");
            var accountIndex = database.Phi.AccountIndex(accounts[0].Id);
            if (await database.RedStarPatients.AnyAsync(p => p.PortalUserIdBlindIndex == accountIndex, cancellationToken))
                return WriteResult.Invalid("That portal account is already linked to a clinic record.");
            await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Enrollment.PatientId == id), false, cancellationToken);
            patient.PortalUserId = accounts[0].Id; patient.EmailRemindersEnabled = false;
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Verify portal link", $"Patient/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return await PatientResult(id, result, "Portal account linked. The patient can enable email reminders in the portal.", cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Unlink([FromRoute] int id, RedStarUnlinkInput input, CancellationToken cancellationToken)
    {
        if (!input.IdentityVerified) ModelState.AddModelError("", "Verify the clinic record before removing its portal link.");
        if (!ModelState.IsValid) return await PatientResult(id, WriteResult.Invalid(ValidationMessage()), "", cancellationToken);
        var result = await writes.RunAsync(async database =>
        {
            var patient = await database.RedStarPatients.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (patient is null || !Matches(patient.RowVersion, input.PatientVersion)) return WriteResult.Stale;
            if (patient.PortalUserId is null) return WriteResult.Invalid("This clinic record has no portal link.");
            await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Enrollment.PatientId == id), false, cancellationToken);
            patient.PortalUserId = null; patient.EmailRemindersEnabled = false;
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Unlink portal account", $"Patient/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return await PatientResult(id, result, "Portal link removed and outstanding reminders cancelled.", cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Schedule([FromRoute] int id, RedStarScheduleInput input, CancellationToken cancellationToken)
    {
        ValidateSchedule(input);
        if (!ModelState.IsValid) return Error(id, ValidationMessage());
        var result = await writes.RunAsync(async database =>
        {
            var enrollment = await database.RedStarEnrollments.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (enrollment is null || !Matches(enrollment.RowVersion, input.EnrollmentVersion)) return WriteResult.Stale;
            if (enrollment.Status != RedStarStatus.Active) return WriteResult.Invalid("Verify and activate the enrollment before scheduling care.");
            if (input.Kind == RedStarAppointmentKind.Refill && await database.RedStarAppointments.AnyAsync(a => a.EnrollmentId == id && a.Kind == input.Kind && a.State == RedStarAppointmentState.Scheduled, cancellationToken))
                return WriteResult.Invalid("A refill is already scheduled. Update or cancel its existing date first.");
            database.RedStarAppointments.Add(new RedStarAppointment
            {
                EnrollmentId = id, Kind = input.Kind, DueDate = WallDate(input.DueDate!.Value), Label = Clean(input.Label),
                RemindersVerified = input.RemindersVerified, State = RedStarAppointmentState.Scheduled
            });
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Schedule care", $"Enrollment/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return Result(id, result, "Care date scheduled. Reminders run only for a verified refill date.");
    }

    [HttpPost]
    public async Task<IActionResult> UpdateAppointment([FromRoute] int id, RedStarScheduleInput input, CancellationToken cancellationToken)
    {
        ValidateSchedule(input, allowHistorical: true);
        if (AdminAudit.Version(input.AppointmentVersion) is null) ModelState.AddModelError("", "Reload the appointment before updating it.");
        if (!ModelState.IsValid) return await AppointmentError(id, ValidationMessage(), cancellationToken);
        var enrollmentId = 0;
        var result = await writes.RunAsync(async database =>
        {
            var appointment = await database.RedStarAppointments.Include(a => a.Enrollment).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (appointment is null) return WriteResult.Stale;
            enrollmentId = appointment.EnrollmentId;
            if (!Matches(appointment.RowVersion, input.AppointmentVersion) || !Matches(appointment.Enrollment.RowVersion, input.EnrollmentVersion)) return WriteResult.Stale;
            if (appointment.State != RedStarAppointmentState.Scheduled || appointment.Enrollment.Status != RedStarStatus.Active)
                return WriteResult.Invalid("Only a scheduled date in an active enrollment can be updated.");
            if (input.Kind != appointment.Kind) return WriteResult.Invalid("Create a separate appointment to change its kind.");
            if (input.DueDate!.Value.Date != appointment.DueDate.Date && !ValidFutureDate(input.DueDate))
                return WriteResult.Invalid("A replacement care date must be today or later. An existing overdue date may be verified without moving it.");
            await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Id == id), false, cancellationToken);
            appointment.DueDate = WallDate(input.DueDate!.Value); appointment.Label = Clean(input.Label); appointment.RemindersVerified = input.RemindersVerified;
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Update care date", $"Appointment/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return enrollmentId == 0 ? NotFound() : Result(enrollmentId, result, "Care date updated; earlier reminders, confirmation and requests were cancelled.");
    }

    [HttpPost]
    public async Task<IActionResult> CancelAppointment([FromRoute] int id, RedStarAppointmentCommand input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await AppointmentError(id, ValidationMessage(), cancellationToken);
        var enrollmentId = 0;
        var result = await writes.RunAsync(async database =>
        {
            var appointment = await database.RedStarAppointments.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (appointment is null) return WriteResult.Stale;
            enrollmentId = appointment.EnrollmentId;
            if (!Matches(appointment.RowVersion, input.AppointmentVersion)) return WriteResult.Stale;
            if (appointment.State != RedStarAppointmentState.Scheduled) return WriteResult.Invalid("Only a scheduled appointment can be cancelled.");
            await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Id == id), true, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Cancel care date", $"Appointment/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return enrollmentId == 0 ? NotFound() : Result(enrollmentId, result, "Appointment and its pending reminders and requests cancelled.");
    }

    [HttpPost]
    public async Task<IActionResult> Dispense([FromRoute] int id, RedStarDispenseInput input, CancellationToken cancellationToken)
    {
        if (!ValidPastDate(input.DispensedDate)) ModelState.AddModelError(nameof(input.DispensedDate), "Enter a dispensing date on or before today.");
        if (input.NextRefillDate.HasValue && (!ValidFutureDate(input.NextRefillDate) || input.NextRefillDate.Value.Date <= input.DispensedDate?.Date))
            ModelState.AddModelError(nameof(input.NextRefillDate), "The next refill date must be today or later and after dispensing.");
        if (input.NextRefillDate.HasValue && !input.NextDateVerified) ModelState.AddModelError("", "Verify the approved next refill date or leave it blank.");
        if (!ModelState.IsValid) return await AppointmentError(id, ValidationMessage(), cancellationToken);
        var enrollmentId = 0;
        var result = await writes.RunAsync(async database =>
        {
            var appointment = await database.RedStarAppointments.Include(a => a.Enrollment).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (appointment is null) return WriteResult.Stale;
            enrollmentId = appointment.EnrollmentId;
            if (!Matches(appointment.RowVersion, input.AppointmentVersion) || !Matches(appointment.Enrollment.RowVersion, input.EnrollmentVersion)) return WriteResult.Stale;
            if (appointment.Kind != RedStarAppointmentKind.Refill || appointment.State != RedStarAppointmentState.Scheduled || appointment.Enrollment.Status != RedStarStatus.Active)
                return WriteResult.Invalid("Only a scheduled refill for an active enrollment can be dispensed.");
            if (await database.RedStarDispenses.AnyAsync(d => d.AppointmentId == id, cancellationToken)) return WriteResult.Invalid("This refill has already been recorded.");
            if (input.NextRefillDate.HasValue && await database.RedStarAppointments.AnyAsync(a => a.EnrollmentId == enrollmentId && a.Id != id &&
                a.Kind == RedStarAppointmentKind.Refill && a.State == RedStarAppointmentState.Scheduled, cancellationToken))
                return WriteResult.Invalid("Another refill date is already scheduled. Record dispensing without creating another date.");
            await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Id == id), false, cancellationToken);
            appointment.State = RedStarAppointmentState.PickedUp;
            var dispensed = WallDate(input.DispensedDate!.Value);
            database.RedStarDispenses.Add(new RedStarDispense
            {
                EnrollmentId = enrollmentId, AppointmentId = id, DispensedDate = dispensed, DaysSupplied = input.DaysSupplied,
                EstimatedSupplyEnd = input.DaysSupplied.HasValue ? dispensed.AddDays(input.DaysSupplied.Value) : null,
                Medicines = input.Medicines.Trim(), RecordedById = Actor
            });
            appointment.Enrollment.Medicines = input.Medicines.Trim();
            if (input.NextRefillDate.HasValue)
                database.RedStarAppointments.Add(new RedStarAppointment
                {
                    EnrollmentId = enrollmentId, Kind = RedStarAppointmentKind.Refill, DueDate = WallDate(input.NextRefillDate.Value),
                    State = RedStarAppointmentState.Scheduled, RemindersVerified = true
                });
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, "Record dispensing", $"Appointment/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return enrollmentId == 0 ? NotFound() : Result(enrollmentId, result, "Dispensing recorded. Supply estimate and the approved next visit are stored separately.");
    }

    [HttpPost]
    public async Task<IActionResult> DecideRequest([FromRoute] int id, RedStarRequestDecision input, CancellationToken cancellationToken)
    {
        if (input.Approve && input.ApprovedDate.HasValue && !ValidFutureDate(input.ApprovedDate)) ModelState.AddModelError("", "Choose an approved date today or later.");
        if (!ModelState.IsValid)
        {
            var target = await db.RedStarRescheduleRequests.Where(r => r.Id == id).Select(r => (int?)r.Appointment.EnrollmentId).SingleOrDefaultAsync(cancellationToken);
            return target.HasValue ? Error(target.Value, ValidationMessage()) : NotFound();
        }
        var enrollmentId = 0;
        var result = await writes.RunAsync(async database =>
        {
            var request = await database.RedStarRescheduleRequests.Include(r => r.Appointment).ThenInclude(a => a.Enrollment)
                .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (request is null) return WriteResult.Stale;
            var appointment = request.Appointment; enrollmentId = appointment.EnrollmentId;
            if (!Matches(request.RowVersion, input.RequestVersion) || !Matches(appointment.RowVersion, input.AppointmentVersion) ||
                appointment.Revision != input.AppointmentRevision || request.AppointmentRevision != appointment.Revision) return WriteResult.Stale;
            if (request.State != RedStarRequestState.Pending || appointment.State != RedStarAppointmentState.Scheduled)
                return WriteResult.Invalid("This request no longer applies to a scheduled appointment.");
            if (input.Approve)
            {
                var date = input.ApprovedDate ?? request.RequestedDate;
                if (!ValidFutureDate(date)) return WriteResult.Invalid("Choose an approved date today or later.");
                if (appointment.Enrollment.Status != RedStarStatus.Active) return WriteResult.Invalid("The enrollment must be active before approving a new date.");
                await InvalidateAppointments(database, database.RedStarAppointments.Where(a => a.Id == appointment.Id), false, cancellationToken);
                appointment.DueDate = WallDate(date); appointment.RemindersVerified = true;
                request.State = RedStarRequestState.Approved;
            }
            else request.State = RedStarRequestState.Declined;
            await database.SaveChangesAsync(cancellationToken);
            await RedStarAuditTrail.RecordAsync(database, Actor, input.Approve ? "Approve reschedule" : "Decline reschedule", $"Request/{id}", cancellationToken);
            return WriteResult.Success;
        }, cancellationToken);
        return enrollmentId == 0 ? NotFound() : Result(enrollmentId, result, input.Approve ? "Reschedule approved. Earlier reminders and confirmation were cancelled." : "Reschedule declined; the existing date remains scheduled.");
    }

    [HttpGet]
    public async Task<IActionResult> Audit(int page = 1, CancellationToken cancellationToken = default)
    {
        var query = db.RedStarAudits.AsNoTracking();
        var total = await query.CountAsync(cancellationToken); page = Page(page, total);
        var rows = await query.OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View private audit", "Audit", cancellationToken);
        return View(new RedStarAuditPage(rows, page, total));
    }

    [HttpGet]
    public async Task<IActionResult> Deliveries(int page = 1, int refillPage = 1, CancellationToken cancellationToken = default)
    {
        var query = db.RedStarReminders.AsNoTracking();
        var total = await query.CountAsync(cancellationToken); page = Page(page, total);
        var rows = await query.OrderByDescending(r => r.CreatedUtc).ThenBy(r => r.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(r => new RedStarDeliveryRow(r.Id, r.Appointment.Enrollment.Patient.PatientCode, r.Appointment.DueDate, r.Stage,
                r.Channel, r.State, r.Attempts, r.CreatedUtc, r.CompletedUtc,
                r.Appointment.State == RedStarAppointmentState.Scheduled && r.Appointment.Kind == RedStarAppointmentKind.Refill &&
                r.Appointment.Enrollment.Status == RedStarStatus.Active && r.Appointment.Enrollment.Program != RedStarProgram.Unverified &&
                r.AppointmentRevision == r.Appointment.Revision && r.State != RedStarDeliveryState.Cancelled &&
                r.PortalUserIdBlindIndex != null && r.PortalUserIdBlindIndex == r.Appointment.Enrollment.Patient.PortalUserIdBlindIndex,
                r.Appointment.RemindersVerified)).ToListAsync(cancellationToken);
        var refills = OutstandingRefills();
        var refillTotal = await refills.CountAsync(cancellationToken); refillPage = Page(refillPage, refillTotal);
        var refillRows = await refills.OrderBy(a => a.DueDate).ThenBy(a => a.Id).Skip((refillPage - 1) * PageSize).Take(PageSize)
            .Select(a => new RedStarRefillWorklistRow(a.EnrollmentId, a.Enrollment.Patient.PatientCode,
                a.Enrollment.Patient.CodeName, a.DueDate, a.RemindersVerified)).ToListAsync(cancellationToken);
        await RedStarAuditTrail.RecordAsync(db, Actor, "View delivery status", "Deliveries", cancellationToken);
        return View(new RedStarDeliveriesPage(rows, page, total, refillRows, refillPage, refillTotal, ClinicalClock.Today));
    }

    private IQueryable<RedStarAppointment> OutstandingRefills() => db.RedStarAppointments.AsNoTracking()
        .Where(a => a.Kind == RedStarAppointmentKind.Refill && a.State == RedStarAppointmentState.Scheduled &&
            a.Enrollment.Status == RedStarStatus.Active && a.Enrollment.Program != RedStarProgram.Unverified);

    private static async Task InvalidateAppointments(ApplicationDbContext database, IQueryable<RedStarAppointment> query, bool close, CancellationToken token)
    {
        await RedStarAuditTrail.CancelRemindersAsync(database, query, token);
        var scheduled = await query.Where(a => a.State == RedStarAppointmentState.Scheduled).ToListAsync(token);
        var ids = scheduled.Select(a => a.Id).ToArray();
        var requests = await database.RedStarRescheduleRequests.Where(r => ids.Contains(r.AppointmentId) && r.State == RedStarRequestState.Pending).ToListAsync(token);
        foreach (var request in requests) request.State = RedStarRequestState.Declined;
        foreach (var appointment in scheduled)
        {
            appointment.Revision++; appointment.ConfirmedUtc = null;
            if (close) appointment.State = RedStarAppointmentState.Cancelled;
        }
    }

    private void ValidateEnrollment(RedStarEnrollmentInput input)
    {
        if (!string.IsNullOrEmpty(input.WorksheetLayout) && RedStarRegistryLayout.Find(input.WorksheetLayout) is null)
            ModelState.AddModelError(nameof(input.WorksheetLayout), "Select a supported worksheet layout.");
        if (!Enum.IsDefined(input.Program) || !Enum.IsDefined(input.Status)) ModelState.AddModelError("", "Select a supported program and status.");
        if (input.Status == RedStarStatus.Active && input.Program == RedStarProgram.Unverified) ModelState.AddModelError("", "Verify the program before activating the enrollment.");
        if (input.EnrolledDate.HasValue && !ValidPastDate(input.EnrolledDate) || input.StartDate.HasValue && !ValidPastDate(input.StartDate) ||
            input.StatusDate.HasValue && !ValidPastDate(input.StatusDate)) ModelState.AddModelError("", "Enrollment, start and status dates must be valid dates on or before today.");
        if (input.Status is RedStarStatus.TransferredOut or RedStarStatus.Discontinued or RedStarStatus.Completed or RedStarStatus.Deceased && !input.StatusDate.HasValue)
            ModelState.AddModelError(nameof(input.StatusDate), "Record the date of this status change.");
        if (input.Status == RedStarStatus.TransferredOut && string.IsNullOrWhiteSpace(input.ReceivingHub)) ModelState.AddModelError(nameof(input.ReceivingHub), "Record the receiving hub.");
    }
    private void ValidateSchedule(RedStarScheduleInput input, bool allowHistorical = false)
    {
        if (!Enum.IsDefined(input.Kind)) ModelState.AddModelError(nameof(input.Kind), "Select a supported appointment kind.");
        if (allowHistorical)
        {
            if (!input.DueDate.HasValue || input.DueDate.Value.Date < new DateTime(1900, 1, 1) || input.DueDate.Value.Date > new DateTime(2200, 12, 31))
                ModelState.AddModelError(nameof(input.DueDate), "Enter a valid care date.");
        }
        else if (!ValidFutureDate(input.DueDate)) ModelState.AddModelError(nameof(input.DueDate), "Choose a care date today or later.");
    }
    private static bool ValidPastDate(DateTime? date) => date.HasValue && date.Value.Date >= new DateTime(1900, 1, 1) && date.Value.Date <= ClinicalClock.Today;
    private static bool ValidFutureDate(DateTime? date) => date.HasValue && date.Value.Date >= ClinicalClock.Today && date.Value.Date <= ClinicalClock.Today.AddYears(10);
    private static bool Matches(byte[] current, string? encoded) => AdminAudit.Version(encoded) is { } version && current.SequenceEqual(version);
    private static string Version(byte[] version) => Convert.ToBase64String(version);
    private static int Page(int requested, int total) => Math.Clamp(requested, 1, Math.Max(1, (total + PageSize - 1) / PageSize));
    private static DateTime WallDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ApplyPatient(RedStarEnrollmentInput input, RedStarPatient patient)
    {
        patient.PatientCode = input.PatientCode.Trim(); patient.CodeName = input.CodeName?.Trim() ?? ""; patient.FullName = input.FullName.Trim();
        patient.HospitalNumber = Clean(input.HospitalNumber); patient.Address = Clean(input.Address); patient.ContactNumber = Clean(input.ContactNumber);
    }
    private static void ApplyEnrollment(RedStarEnrollmentInput input, RedStarEnrollment enrollment)
    {
        enrollment.Program = input.Program; enrollment.Status = input.Status; enrollment.Medicines = input.Medicines?.Trim() ?? "";
        enrollment.EnrolledDate = input.EnrolledDate.HasValue ? WallDate(input.EnrolledDate.Value) : null;
        enrollment.StartDate = input.StartDate.HasValue ? WallDate(input.StartDate.Value) : null;
        enrollment.StatusDate = input.StatusDate.HasValue ? WallDate(input.StatusDate.Value) : null;
        enrollment.ReceivingHub = input.Status == RedStarStatus.TransferredOut ? Clean(input.ReceivingHub) : null;
    }
    private string ValidationMessage() => string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Review the form fields." : e.ErrorMessage).Distinct().Take(4));
    private IActionResult Success(int id, string message) { TempData["RedStarSuccess"] = message; return RedirectToAction(nameof(Details), new { id }); }
    private IActionResult Error(int id, string message) { TempData["RedStarError"] = message; return RedirectToAction(nameof(Details), new { id }); }
    private IActionResult Result(int id, WriteResult result, string success) => result.Succeeded ? Success(id, success) : Error(id, result.Error!);
    private async Task<IActionResult> PatientResult(int patientId, WriteResult result, string message, CancellationToken token)
    {
        var id = await db.RedStarEnrollments.Where(e => e.PatientId == patientId).OrderBy(e => e.Id).Select(e => (int?)e.Id).FirstOrDefaultAsync(token);
        if (!id.HasValue) return NotFound();
        return Result(id.Value, result, message);
    }
    private async Task<IActionResult> AppointmentError(int appointmentId, string message, CancellationToken token)
    {
        var id = await db.RedStarAppointments.Where(a => a.Id == appointmentId).Select(a => (int?)a.EnrollmentId).SingleOrDefaultAsync(token);
        return id.HasValue ? Error(id.Value, message) : NotFound();
    }
}
