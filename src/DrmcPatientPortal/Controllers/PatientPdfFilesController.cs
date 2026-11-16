using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientPdfFilesController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    PatientPdfDelivery delivery, IOptionsMonitor<PatientResultsOptions> options, ApplicationDbContext db,
    RedStarReportAccess redStar) : Controller
{
    [HttpGet("Patient/ReportFiles", Name = PatientPdfDelivery.RouteName)]
    public async Task<IActionResult> Open(string? ticket, CancellationToken token)
    {
        if (ticket is null || !options.CurrentValue.ShowFullResults) return NotFound();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await users.IsLockedOutAsync(user) || !await signIn.CanSignInAsync(user)) return NotFound();
        var report = delivery.Take(ticket, user);
        if (report is null || !await IsCurrentAsync(user.Id, report, token)) return NotFound();
        return report.File;
    }

    private async Task<bool> IsCurrentAsync(string userId, PatientPdfDelivery.Report report, CancellationToken token)
    {
        var now = PatientResultsDisclosure.ManilaNow;
        switch (report.Source)
        {
            case PatientPdfSource.Laboratory:
                var labName = await SqlReadRetry.RunAsync(db, () => db.LabResults.AsNoTracking()
                    .Where(PatientResultsDisclosure.PdfEligibility(true, now))
                    .Where(l => l.Id == report.Id && l.Patient.PortalUserId == userId)
                    .Select(l => l.ReportFileName).SingleOrDefaultAsync(token), token);
                return report.StoredFileName == labName;
            case PatientPdfSource.Radiology:
                var radiologyName = await SqlReadRetry.RunAsync(db, () => db.RadiologyStudies.AsNoTracking()
                    .Where(PatientResultsDisclosure.RadiologyPdfEligibility(true, now))
                    .Where(r => r.Id == report.Id && r.Patient.PortalUserId == userId)
                    .Select(r => r.ReportFileName).SingleOrDefaultAsync(token), token);
                return report.StoredFileName == radiologyName;
            case PatientPdfSource.RedStar:
                var privateReport = await redStar.GetOwnedReportAsync(userId, report.Id, token);
                return privateReport is not null && privateReport.ReportFileName == report.StoredFileName &&
                    RedStarReportAccess.CanOpenPdf(privateReport, true, now);
            default:
                return false;
        }
    }
}
