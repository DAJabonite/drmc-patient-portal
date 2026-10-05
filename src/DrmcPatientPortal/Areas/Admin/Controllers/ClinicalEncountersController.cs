using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class ClinicalEncountersController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<ClinicalEncounter, EncounterInput>(db, writes, new EncounterEditor());
