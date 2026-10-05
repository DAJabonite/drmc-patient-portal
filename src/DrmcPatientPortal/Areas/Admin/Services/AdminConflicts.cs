using Microsoft.AspNetCore.Mvc;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class AdminConflicts
{
    public static IActionResult Missing(Controller controller)
    {
        controller.Response.StatusCode = StatusCodes.Status409Conflict;
        return controller.View("/Areas/Admin/Views/Shared/Conflict.cshtml");
    }
}
