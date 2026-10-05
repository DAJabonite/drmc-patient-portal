using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class MedicationDoseSchedulesController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<MedicationDoseSchedule, DoseInput>(db, writes, new DoseEditor());
