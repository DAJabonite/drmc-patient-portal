using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class LabResultsController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<LabResult, LabInput>(db, writes, new LabEditor());
