using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PrescriptionsController(ApplicationDbContext db, AdminWrites writes, PrescriptionAccess access,
    IStringLocalizer<SharedResource> localizer) :
    AdminCrudController<Prescription, PrescriptionInput>(db, writes, new PrescriptionEditor(access))
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
        if (!await access.InitializeAsync(Database, context.HttpContext.RequestAborted) ||
            !access.IsAdmin && action == "Delete")
        { context.Result = new ForbidResult(); return; }
        foreach (var entry in ModelState.Values)
        {
            var messages = entry.Errors.Select(e => e.ErrorMessage).ToArray();
            entry.Errors.Clear();
            foreach (var message in messages) entry.Errors.Add(localizer[message]);
        }
        await next();
    }
}
