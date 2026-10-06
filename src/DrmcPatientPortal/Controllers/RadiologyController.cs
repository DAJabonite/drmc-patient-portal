using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[Route("Patient/Radiology")]
public class RadiologyController : Controller
{
    // GET /Patient/Radiology
    [HttpGet("")]
    public IActionResult Index() => View();
}
