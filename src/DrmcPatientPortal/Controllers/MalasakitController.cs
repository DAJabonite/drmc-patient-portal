using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Controllers;

public class MalasakitController(InstitutionalGuideService guides) : Controller
{
    // GET /Malasakit
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await guides.GetAsync("malasakit", cancellationToken: cancellationToken));

    // GET /Malasakit/DSWDServices
    [HttpGet]
    public async Task<IActionResult> DSWDServices(CancellationToken cancellationToken) =>
        View(await guides.GetAsync("dswd", cancellationToken: cancellationToken));

    // GET /Malasakit/DRMCMalasakitServices
    [HttpGet]
    public async Task<IActionResult> DRMCMalasakitServices(CancellationToken cancellationToken) =>
        View(await guides.GetAsync("drmc-malasakit", cancellationToken: cancellationToken));

    // GET /Malasakit/MaifipRequirements
    public async Task<IActionResult> MaifipRequirements(CancellationToken cancellationToken) =>
        View(await guides.GetAsync("maifip", cancellationToken: cancellationToken));

    // GET /Malasakit/Philhealth
    public async Task<IActionResult> Philhealth(CancellationToken cancellationToken) =>
        View(await guides.GetAsync("philhealth", cancellationToken: cancellationToken));

}
