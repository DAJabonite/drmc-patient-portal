using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DrmcPatientPortal.Controllers;

// APPOINTMENTS MODULE: BACKBURNED / RETIRED PER CLIENT REQUEST
// Online appointment scheduling and check-in workflows are retired from active scope.
// Outpatient visits are managed on-site according to the OPD Guide.
[ApiExplorerSettings(IgnoreApi = true)]
public class AppointmentsController : Controller
{
    public AppointmentsController(
        ApplicationDbContext? db = null,
        UserManager<ApplicationUser>? userManager = null,
        ISmsSender? smsSender = null,
        IEmailSender? emailSender = null,
        IQrCodeService? qrCodeService = null,
        IAppointmentAccessService? appointmentAccess = null)
    {
    }

    [HttpGet]
    public IActionResult Book(string? department = null, int? doctorId = null, string? type = null) => NotFound();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Book(BookingFormViewModel model) => NotFound();

    [HttpGet]
    public IActionResult Access(string token, string? returnUrl = null) => NotFound();

    [HttpGet]
    public IActionResult Confirmation(string? reference = null) => NotFound();

    [HttpGet]
    public IActionResult CheckIn(string? reference = null) => NotFound();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(int id) => NotFound();

    [HttpGet]
    [Route("Appointments/Api/Doctors")]
    public IActionResult Doctors(string department) => NotFound();

    [HttpGet]
    [Route("Appointments/Api/AvailableSlots")]
    public IActionResult AvailableSlots(string department, int? doctorId = null, DateTime? date = null) => NotFound();
}

public class BookingFormViewModel
{
    [Required(ErrorMessage = "Please enter the patient's full name.")]
    [Display(Name = "Patient Full Name")]
    public string PatientName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a valid Philippine mobile number (09xx).")]
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
