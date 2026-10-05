using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class LabResultItemsController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<LabResultItem, LabItemInput>(db, writes, new LabItemEditor());
