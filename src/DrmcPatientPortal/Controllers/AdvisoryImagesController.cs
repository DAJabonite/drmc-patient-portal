using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public sealed class AdvisoryImagesController(ApplicationDbContext db, IAdvisoryImageStorage storage,
    IAuthorizationService authorization, TimeProvider clock) : Controller
{
    [HttpGet("/Advisories/Images/{fileName}")]
    public async Task<IActionResult> Image(string fileName, CancellationToken token)
    {
        if (!AdvisoryImageStorage.IsFileName(fileName)) return NotFound();
        var url = AdvisoryImageStorage.UrlPrefix + fileName;
        var now = clock.GetUtcNow().UtcDateTime;
        var published = await db.PublicAdvisories.AsNoTracking().AnyAsync(a => a.ImageUrl == url && a.PublishedAt <= now, token);
        if (!published && (!(await authorization.AuthorizeAsync(User, AdminPolicies.For("PublicAdvisories"))).Succeeded ||
            !await db.PublicAdvisories.AnyAsync(a => a.ImageUrl == url, token))) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        try { return File(await storage.ReadAsync(fileName, token), "image/webp"); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return StatusCode(503); }
    }
}
