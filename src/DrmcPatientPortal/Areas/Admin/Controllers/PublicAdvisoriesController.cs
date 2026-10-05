using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PublicAdvisoriesController(ApplicationDbContext db, AdminWrites writes)
    : AdminCrudController<PublicAdvisory, PublicAdvisoryInput>(db, writes, new AdvisoryEditor());
