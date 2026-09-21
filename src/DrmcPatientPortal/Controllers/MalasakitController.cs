using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Controllers;

public class MalasakitController : Controller
{
    // GET /Malasakit
    public IActionResult Index() => View();

    // GET /Malasakit/DSWDServices
    [HttpGet]
    public IActionResult DSWDServices()
    {
        return View();
    }

    // GET /Malasakit/DRMCMalasakitServices
    [HttpGet]
    public IActionResult DRMCMalasakitServices()
    {
        return View();
    }

    // GET /Malasakit/MaifipRequirements
    public IActionResult MaifipRequirements()
    {
        return View();
    }

    // GET /Malasakit/Philhealth
    public IActionResult Philhealth()
    {
        return View();
    }

}
