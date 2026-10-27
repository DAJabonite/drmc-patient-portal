using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[RequestSizeLimit(AdvisoryImageStorage.MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = AdvisoryImageStorage.MaximumRequestBytes)]
public sealed class PublicAdvisoriesController(ApplicationDbContext db, AdminWrites writes, IAdvisoryImageStorage storage)
    : AdminCrudController<PublicAdvisory, PublicAdvisoryInput>(db, writes, new AdvisoryEditor())
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.RouteValues["action"] == "Edit" &&
            int.TryParse(context.RouteData.Values["id"]?.ToString(), out var id))
            ViewData["CurrentImageUrl"] = await Database.PublicAdvisories.AsNoTracking()
                .Where(a => a.Id == id).Select(a => a.ImageUrl).FirstOrDefaultAsync(HttpContext.RequestAborted);
        await next();
    }

    protected override async Task<WriteResult> SaveRecordAsync(ApplicationDbContext database, PublicAdvisoryInput input,
        PublicAdvisory entity, CancellationToken token)
    {
        if (input.ImageUpload is not null)
        {
            try { entity.ImageUrl = await storage.StoreAsync(input.ImageUpload, token); }
            catch (InvalidDataException error) { return WriteResult.Invalid(error.Message); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { return WriteResult.Invalid("The image could not be stored. Choose the file again and try saving."); }
        }
        else if (input.RemoveImage) entity.ImageUrl = null;
        // Immutable files are retained on an uncertain commit; only committed references are served.
        await database.SaveChangesAsync(token);
        return WriteResult.Success;
    }
}
