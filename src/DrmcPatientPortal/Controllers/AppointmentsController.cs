using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Controllers;

// Retired URLs remain explicit 404s. Existing appointment rows are retained for triage history.
[ApiExplorerSettings(IgnoreApi = true)]
public class AppointmentsController : Controller
{
    [HttpGet]
    public IActionResult Book() => NotFound();

    [HttpPost, ActionName("Book"), ValidateAntiForgeryToken]
    public IActionResult BookPost() => NotFound();

    [HttpGet]
    public IActionResult Access(string token, string? returnUrl = null) => NotFound();

    [HttpGet]
    public IActionResult Confirmation(string? reference = null) => NotFound();

    [HttpGet]
    public IActionResult CheckIn(string? reference = null) => NotFound();

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancel(int id) => NotFound();

    [HttpGet("Appointments/Api/Doctors")]
    public IActionResult Doctors(string department) => NotFound();

    [HttpGet("Appointments/Api/AvailableSlots")]
    public IActionResult AvailableSlots(string department, int? doctorId = null, DateTime? date = null) => NotFound();
}
