using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Controllers;

[Authorize, Route("Patient/LabResults/RedStar")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RedStarLabResultsController(RedStarReportAccess access, IRedStarLabReportStorage storage,
    ApplicationDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IOptionsMonitor<PatientResultsOptions> options) : Controller
{
    [HttpGet("Details/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken token)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var report = await access.GetOwnedReportAsync(user.Id, id, token);
        if (report is null) return NotFound();
        await RedStarAuditTrail.RecordAsync(db, user.Id, "View owned lab availability", $"LabReport/{id}", token);
        return View(new RedStarPatientReportPage(report, RedStarReportAccess.CanOpenPdf(report, options.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow)));
    }

    [HttpPost("Report/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(int id, string? password, string? intent, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var report = await Eligible(user.Id, id, token);
        if (report is null) return NotFound();
        if (intent is not ("view" or "download")) return BadRequest();
        if (string.IsNullOrEmpty(password) || password.Length > 1024) return await PasswordError(id, "Enter your account password for each View or Download action.", token);
        var locked = await users.IsLockedOutAsync(user);
        if (locked || !await signIn.CanSignInAsync(user) || !await users.CheckPasswordAsync(user, password))
            return await PasswordError(id, locked ? "Your account is temporarily locked. Try again after the lockout period." : "The password could not be verified. Please try again.", token);
        byte[] bytes;
        try { bytes = await storage.ReadAsync(id, report.ReportFileName!, token); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { return StatusCode(503); }
        var fresh = await Eligible(user.Id, id, token);
        if (fresh is null || fresh.ReportFileName != report.ReportFileName) return NotFound();
        try { await RedStarAuditTrail.RecordAsync(db, user.Id, intent == "view" ? "View owned lab PDF" : "Download owned lab PDF", $"LabReport/{id}", token); }
        catch (AuditLogPersistenceException) { return StatusCode(503); }
        fresh = await Eligible(user.Id, id, token);
        if (fresh is null || fresh.ReportFileName != report.ReportFileName || await users.IsLockedOutAsync(user)) return NotFound();
        return new PrivateFileResult(bytes, "application/pdf", $"lab-report-{id}.pdf", intent == "download");
    }
    private async Task<RedStarLabReport?> Eligible(string userId, int id, CancellationToken token)
    {
        var report = await access.GetOwnedReportAsync(userId, id, token);
        return report is not null && RedStarReportAccess.CanOpenPdf(report, options.CurrentValue.ShowFullResults, PatientResultsDisclosure.ManilaNow) ? report : null;
    }
    private async Task<IActionResult> PasswordError(int id, string message, CancellationToken token)
    {
        ModelState.Clear();
        ModelState.AddModelError("", message);
        return await Details(id, token);
    }
}
