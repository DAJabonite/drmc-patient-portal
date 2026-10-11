using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WebsiteUsageController(WebsiteUsageReports reports) : Controller
{
    private bool Valid(WebsiteUsageFilter filter, out WebsiteUsageRange range)
    {
        var valid = filter.TryResolve(reports.Today, out range, out var error);
        if (!valid) ModelState.AddModelError("", error!);
        return valid && ModelState.IsValid;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] WebsiteUsageFilter filter, CancellationToken cancellationToken)
    {
        if (!Valid(filter, out var range))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new WebsiteUsagePage(filter, null));
        }
        return View(new WebsiteUsagePage(filter, await reports.ReadAsync(range, cancellationToken)));
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] WebsiteUsageFilter filter, CancellationToken cancellationToken)
    {
        if (!Valid(filter, out var range)) return BadRequest("Choose valid report dates and a range of no more than 366 days.");
        var report = await reports.ReadAsync(range, cancellationToken);
        return File(WebsiteUsageReports.Csv(report), "text/csv; charset=utf-8", $"website-usage-{range.Start:yyyy-MM-dd}-{range.End:yyyy-MM-dd}.csv");
    }
}
