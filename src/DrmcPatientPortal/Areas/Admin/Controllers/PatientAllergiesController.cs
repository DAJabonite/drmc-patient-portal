using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

public sealed class PatientAllergiesController(ApplicationDbContext db, AdminWrites writes) :
    AdminCrudController<PatientAllergy, AllergyInput>(db, writes, new AllergyEditor());
