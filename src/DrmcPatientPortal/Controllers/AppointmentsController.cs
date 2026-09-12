using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class AppointmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;
    private readonly IQrCodeService _qrCodeService;
    private readonly IAppointmentAccessService _appointmentAccess;

    public AppointmentsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ISmsSender smsSender,
        IEmailSender emailSender,
        IQrCodeService qrCodeService,
        IAppointmentAccessService appointmentAccess)
    {
        _db = db;
        _userManager = userManager;
        _smsSender = smsSender;
        _emailSender = emailSender;
        _qrCodeService = qrCodeService;
        _appointmentAccess = appointmentAccess;
    }

    // GET /Appointments/Book
    [HttpGet]
    public async Task<IActionResult> Book(string? department, int? doctorId, string? type)
    {
        var model = new BookingFormViewModel
        {
            Department = department ?? "Internal Medicine",
            DoctorId = doctorId,
            AppointmentType = string.Equals(type, "Teleconsultation", StringComparison.OrdinalIgnoreCase) 
                ? "Teleconsultation" 
                : "In-Person OPD",
            AppointmentDate = DateTime.Today.AddDays(2),
            TimeSlot = "09:00 AM - 09:30 AM"
        };

        // If user is signed in, prefill profile information
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                model.PatientName = user.FullName;
                model.ContactNumber = user.ContactNumber;
                model.Email = user.Email ?? string.Empty;
                model.PhilHealthNumber = string.Equals(user.IdType, "PhilHealth ID", StringComparison.OrdinalIgnoreCase) ? user.IdNumber : null;
            }
        }

        await PopulateBookingDropdownsAsync(model);
        return View(model);
    }

    // POST /Appointments/Book
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(BookingFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateBookingDropdownsAsync(model);
            return View(model);
        }

        if (model.AppointmentDate.Date <= DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.AppointmentDate), "Please select a consultation date at least 1 day in advance.");
            await PopulateBookingDropdownsAsync(model);
            return View(model);
        }

        if (!ClinicalDepartments.All.Any(d => d.Name == model.Department))
            ModelState.AddModelError(nameof(model.Department), "Select a valid clinical department.");
        if (model.AppointmentType is not ("In-Person OPD" or "Teleconsultation"))
            ModelState.AddModelError(nameof(model.AppointmentType), "Select a valid consultation type.");
        if (!BookingFormViewModel.TimeSlots.Contains(model.TimeSlot))
            ModelState.AddModelError(nameof(model.TimeSlot), "Select a valid appointment time.");

        var slotStartText = model.TimeSlot.Split(" - ", StringSplitOptions.TrimEntries)[0];
        if (!TimeOnly.TryParseExact(slotStartText, "hh:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var slotStart))
            ModelState.AddModelError(nameof(model.TimeSlot), "Select a valid appointment time.");

        Doctor? doctor = null;
        if (model.DoctorId.HasValue)
        {
            doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == model.DoctorId.Value && d.IsActive);
            if (doctor is null || doctor.Department != model.Department)
                ModelState.AddModelError(nameof(model.DoctorId), "Select an active doctor from the chosen department.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateBookingDropdownsAsync(model);
            return View(model);
        }

        var scheduledDateTime = model.AppointmentDate.Date.Add(slotStart.ToTimeSpan());
        if (doctor is not null && await _db.Appointments.AnyAsync(a => a.DoctorId == doctor.Id && a.ScheduledAt == scheduledDateTime && a.Status != "Cancelled"))
        {
            ModelState.AddModelError(nameof(model.TimeSlot), "That doctor is already booked for the selected time.");
            await PopulateBookingDropdownsAsync(model);
            return View(model);
        }

        var doctorName = doctor?.FullName ?? "Attending OPD Specialist";
        var deptPrefix = model.Department.Length >= 2 ? model.Department[..2].ToUpperInvariant() : "OP";
        string bookingRef;
        do { bookingRef = $"DRMC-{DateTime.Now.Year}-{deptPrefix}-{Random.Shared.Next(1000, 10000)}"; }
        while (await _db.Appointments.AnyAsync(a => a.BookingReference == bookingRef));

        var user = User.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;

        var appointment = new Appointment
        {
            BookingReference = bookingRef,
            PatientUserId = user?.Id,
            PatientName = model.PatientName,
            ContactNumber = model.ContactNumber,
            Email = model.Email,
            PhilHealthNumber = model.PhilHealthNumber,
            Department = model.Department,
            DoctorId = doctor?.Id,
            DoctorName = doctorName,
            Type = model.AppointmentType,
            ScheduledAt = scheduledDateTime,
            TimeSlot = model.TimeSlot,
            ChiefComplaint = model.ChiefComplaint,
            Status = "Confirmed",
            QrCodePayload = string.Empty,
            TeleconsultMeetingUrl = model.AppointmentType == "Teleconsultation" 
                ? $"https://telehealth.drmc.doh.gov.ph/consult/room-{bookingRef.ToLowerInvariant()}" 
                : null,
            CreatedAt = DateTime.UtcNow
        };

        var accessToken = _appointmentAccess.CreateToken(appointment);
        appointment.QrCodePayload = Url.Action(nameof(Access), "Appointments", new { reference = bookingRef, token = accessToken, destination = "checkin" }, Request.Scheme)!;

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _appointmentAccess.GrantCookieAccess(HttpContext, appointment, accessToken);

        // Dispatch through the configured environment-specific notification providers.
        var accessUrl = Url.Action(nameof(Access), "Appointments", new { reference = bookingRef, token = accessToken }, Request.Scheme)!;
        var smsText = $"DRMC appointment {bookingRef} is confirmed for {appointment.ScheduledAt:MMM d, yyyy} ({appointment.TimeSlot}). Secure pass: {accessUrl}";
        await _smsSender.SendSmsAsync(appointment.ContactNumber, smsText);

        if (!string.IsNullOrWhiteSpace(appointment.Email))
        {
            var emailSubject = $"DRMC Appointment Confirmation — {bookingRef}";
            var emailBody = $"Your DRMC appointment ({bookingRef}) is confirmed for {appointment.ScheduledAt:dddd, MMMM d, yyyy} at {appointment.TimeSlot}. Open your secure pass: <a href=\"{System.Net.WebUtility.HtmlEncode(accessUrl)}\">View appointment</a>.";
            await _emailSender.SendEmailAsync(appointment.Email, emailSubject, emailBody);
        }

        return RedirectToAction(nameof(Confirmation), new { reference = bookingRef });
    }

    [HttpGet]
    public async Task<IActionResult> Access(string reference, string token, string? destination = null)
    {
        var appointment = await _db.Appointments.FirstOrDefaultAsync(a => a.BookingReference == reference);
        if (appointment is null || !_appointmentAccess.ValidateToken(appointment, token)) return NotFound();
        _appointmentAccess.GrantCookieAccess(HttpContext, appointment, token);
        return destination == "checkin"
            ? RedirectToAction(nameof(CheckIn), new { reference })
            : RedirectToAction(nameof(Confirmation), new { reference });
    }

    // GET /Appointments/Confirmation/{reference}
    [HttpGet]
    public async Task<IActionResult> Confirmation(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return RedirectToAction(nameof(Book));
        }

        var appointment = await _db.Appointments
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.BookingReference == reference);

        if (appointment is null)
        {
            return NotFound();
        }

        if (!CanAccessAppointment(appointment)) return NotFound();

        var qrSvg = _qrCodeService.GenerateSvgQrCode(appointment.QrCodePayload);

        var model = new AppointmentConfirmationViewModel
        {
            Appointment = appointment,
            QrCodeSvg = qrSvg
        };

        return View(model);
    }

    // GET /Appointments/CheckIn/{reference}
    [HttpGet]
    public async Task<IActionResult> CheckIn(string reference)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.BookingReference == reference);

        // Use the same response for missing and inaccessible references. Keep
        // the 404 boundary while giving patients a readable recovery screen.
        if (appointment is null || !CanAccessAppointment(appointment))
            return new ViewResult { ViewName = "CheckInNotFound", StatusCode = StatusCodes.Status404NotFound };

        return View(appointment);
    }

    // POST /Appointments/Cancel/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var appointment = await _db.Appointments.FindAsync(id);
        if (appointment is null)
        {
            return NotFound();
        }

        if (!CanAccessAppointment(appointment)) return NotFound();

        appointment.Status = "Cancelled";
        appointment.PublicAccessTokenHash = null;
        appointment.PublicAccessExpiresAt = null;
        await _db.SaveChangesAsync();
        _appointmentAccess.RevokeCookieAccess(HttpContext, appointment);

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Home", "Patient");
        }

        TempData["SuccessMessage"] = "The appointment was cancelled.";
        return RedirectToAction("Index", "Home");
    }

    private bool CanAccessAppointment(Appointment appointment)
    {
        var userId = _userManager.GetUserId(User);
        return (!string.IsNullOrWhiteSpace(userId) && appointment.PatientUserId == userId)
            || _appointmentAccess.HasCookieAccess(HttpContext, appointment);
    }

    private async Task PopulateBookingDropdownsAsync(BookingFormViewModel model)
    {
        var departments = ClinicalDepartments.All;
        model.DepartmentList = departments.Select(d => new SelectListItem
        {
            Value = d.Name,
            Text = d.Name,
            Selected = d.Name == model.Department
        }).ToList();

        var doctors = await _db.Doctors
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .ToListAsync();

        model.DoctorList = doctors.Select(d => new SelectListItem
        {
            Value = d.Id.ToString(),
            Text = $"{d.FullName} ({d.Department} — {d.SubSpecialty})",
            Selected = d.Id == model.DoctorId
        }).ToList();
    }
}

public class BookingFormViewModel
{
    [Required(ErrorMessage = "Please enter the patient's full name.")]
    [Display(Name = "Patient Full Name")]
    [StringLength(100, MinimumLength = 3)]
    public string PatientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a valid Philippine mobile number.")]
    [Display(Name = "Mobile Contact Number")]
    [Phone]
    public string ContactNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a valid email address for confirmation.")]
    [Display(Name = "Email Address")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "PhilHealth ID Number (Optional)")]
    public string? PhilHealthNumber { get; set; }

    [Required(ErrorMessage = "Please select a clinical department.")]
    [Display(Name = "Clinical Department")]
    public string Department { get; set; } = "Internal Medicine";

    [Display(Name = "Preferred Doctor / Specialist (Optional)")]
    public int? DoctorId { get; set; }

    [Required]
    [Display(Name = "Consultation Type")]
    public string AppointmentType { get; set; } = "In-Person OPD";

    [Required(ErrorMessage = "Please select an appointment date.")]
    [Display(Name = "Preferred Date")]
    [DataType(DataType.Date)]
    public DateTime AppointmentDate { get; set; } = DateTime.Today.AddDays(2);

    [Required(ErrorMessage = "Please select a time slot.")]
    [Display(Name = "Time Slot")]
    public string TimeSlot { get; set; } = "09:00 AM - 09:30 AM";

    [Required(ErrorMessage = "Please describe the reason for your visit or symptoms.")]
    [Display(Name = "Chief Complaint / Reason for Visit")]
    [StringLength(500, MinimumLength = 5)]
    public string ChiefComplaint { get; set; } = string.Empty;

    public List<SelectListItem> DepartmentList { get; set; } = new();
    public List<SelectListItem> DoctorList { get; set; } = new();

    public static readonly IReadOnlyList<string> TimeSlots = new List<string>
    {
        "08:00 AM - 08:30 AM",
        "08:30 AM - 09:00 AM",
        "09:00 AM - 09:30 AM",
        "09:30 AM - 10:00 AM",
        "10:00 AM - 10:30 AM",
        "10:30 AM - 11:00 AM",
        "11:00 AM - 11:30 AM",
        "01:00 PM - 01:30 PM",
        "01:30 PM - 02:00 PM",
        "02:00 PM - 02:30 PM",
        "02:30 PM - 03:00 PM",
        "03:00 PM - 03:30 PM",
        "03:30 PM - 04:00 PM"
    };
}

public class AppointmentConfirmationViewModel
{
    public Appointment Appointment { get; set; } = null!;
    public string QrCodeSvg { get; set; } = string.Empty;
}
