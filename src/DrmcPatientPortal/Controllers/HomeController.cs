using System.Diagnostics;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View(new LandingViewModel
        {
            Departments = ClinicalDepartments.All,
            DepartmentHeading = ClinicalDepartments.Heading,
            DepartmentSubheading = ClinicalDepartments.Subheading,
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string returnUrl)
    {
        Response.Cookies.Append(
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
            Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = false
            }
        );

        if (!string.IsNullOrEmpty(returnUrl) && (Url == null || Url.IsLocalUrl(returnUrl)))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

public class LandingViewModel
{
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
    public string DepartmentHeading { get; set; } = string.Empty;
    public string DepartmentSubheading { get; set; } = string.Empty;
}
