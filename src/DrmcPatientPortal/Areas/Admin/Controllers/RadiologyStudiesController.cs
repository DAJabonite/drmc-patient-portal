using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class RadiologyStudiesController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<RadiologyStudy, RadiologyInput>(db, writes, new RadiologyEditor());
