using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
