using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PrescriptionsController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<Prescription, PrescriptionInput>(db, writes, new PrescriptionEditor());
