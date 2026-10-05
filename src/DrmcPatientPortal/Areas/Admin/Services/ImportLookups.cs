using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class ImportLookups
{
    private const string Collation = "Latin1_General_100_CI_AS";
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, PatientRecord> Patients { get; } = [];
    public Dictionary<int, ClinicalEncounter> Encounters { get; } = [];
    public Dictionary<int, LabResult> Labs { get; } = [];
    public Dictionary<int, Prescription> Prescriptions { get; } = [];
    public Dictionary<string, List<object>> References { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Exists(string key) => keys.Contains(key.TrimEnd());
    public void Track(object entity)
    {
        switch (entity)
        {
            case ClinicalEncounter encounter: Encounters[encounter.Id] = encounter; break;
            case LabResult lab: Labs[lab.Id] = lab; break;
            case Prescription rx: Prescriptions[rx.Id] = rx; break;
        }
    }
    private void Reference(string entity, string key, object value)
    {
        var reference = entity + ":" + key.TrimEnd();
        if (!References.TryGetValue(reference, out var matches)) References[reference] = matches = [];
        matches.Add(value);
    }
    public static async Task<ImportLookups> LoadAsync(ApplicationDbContext db, IReadOnlyList<ImportRow> rows, ImportEnvelope envelope, CancellationToken token)
    {
        var result = new ImportLookups();
        string[] Values(string template, string field) => rows.Where(row => row.Template == template).Select(row => row.Fields.GetValueOrDefault(field, "").Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] References(string field) => rows.Select(row => row.Fields.GetValueOrDefault(field, "").Trim()).Where(value => value.StartsWith("existing:", StringComparison.Ordinal)).Select(value => value[9..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ids = envelope.Mappings.Where(mapping => mapping.PatientRecordId.HasValue).Select(mapping => mapping.PatientRecordId!.Value).Distinct().ToArray();
        if (ids.Length > 0) foreach (var patient in await db.PatientRecords.Where(patient => EF.Parameter(ids).Contains(patient.Id)).Select(patient => new PatientRecord { Id = patient.Id }).ToListAsync(token)) result.Patients[patient.Id] = patient;
        var hospitals = Values("Patients", "HospitalNumber").Concat(envelope.Mappings.Where(mapping => mapping.CreateNew).Select(mapping => mapping.Patient.HospitalNumber?.Trim() ?? "")).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hospitals.Length > 0) foreach (var key in await db.PatientRecords.Where(patient => patient.HospitalNumber != null && EF.Parameter(hospitals).Contains(EF.Functions.Collate(patient.HospitalNumber, Collation))).Select(patient => patient.HospitalNumber!).ToListAsync(token)) result.keys.Add("Hospital:" + key.TrimEnd());
        var names = Values("Doctors", "FullName");
        if (names.Length > 0) foreach (var doctor in await db.Doctors.Where(doctor => EF.Parameter(names).Contains(EF.Functions.Collate(doctor.FullName, Collation))).Select(doctor => new { doctor.FullName, doctor.Department }).ToListAsync(token)) result.keys.Add("Doctor:" + doctor.Department.TrimEnd() + ":" + doctor.FullName.TrimEnd());
        var slugs = Values("PublicAdvisories", "Slug");
        if (slugs.Length > 0) foreach (var slug in await db.PublicAdvisories.Where(advisory => EF.Parameter(slugs).Contains(EF.Functions.Collate(advisory.Slug, Collation))).Select(advisory => advisory.Slug).ToListAsync(token)) result.keys.Add("Slug:" + slug.TrimEnd());
        var encounters = Values("ClinicalEncounters", "EncounterReference").Concat(References("SourceEncounterKey")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (encounters.Length > 0) foreach (var encounter in await db.ClinicalEncounters.Where(encounter => EF.Parameter(encounters).Contains(EF.Functions.Collate(encounter.EncounterReference, Collation))).Select(encounter => new ClinicalEncounter { Id = encounter.Id, PatientRecordId = encounter.PatientRecordId, EncounterReference = encounter.EncounterReference }).ToListAsync(token))
        { result.Encounters[encounter.Id] = encounter; result.keys.Add("Encounter:" + encounter.EncounterReference.TrimEnd()); result.Reference("ClinicalEncounters", encounter.EncounterReference, encounter); }
        var labs = Values("LabResults", "AccessionNumber").Concat(References("SourceLabKey")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (labs.Length > 0) foreach (var lab in await db.LabResults.Where(lab => EF.Parameter(labs).Contains(EF.Functions.Collate(lab.AccessionNumber, Collation))).Select(lab => new LabResult { Id = lab.Id, PatientRecordId = lab.PatientRecordId, AccessionNumber = lab.AccessionNumber }).ToListAsync(token))
        { result.Labs[lab.Id] = lab; result.keys.Add("Lab:" + lab.AccessionNumber.TrimEnd()); result.Reference("LabResults", lab.AccessionNumber, lab); }
        var prescriptions = Values("Prescriptions", "RxNumber").Concat(References("SourcePrescriptionKey")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (prescriptions.Length > 0) foreach (var rx in await db.Prescriptions.Where(rx => EF.Parameter(prescriptions).Contains(EF.Functions.Collate(rx.RxNumber, Collation))).Select(rx => new Prescription { Id = rx.Id, PatientRecordId = rx.PatientRecordId, RxNumber = rx.RxNumber }).ToListAsync(token))
        { result.Prescriptions[rx.Id] = rx; result.keys.Add("Rx:" + rx.RxNumber.TrimEnd()); result.Reference("Prescriptions", rx.RxNumber, rx); }
        var labIds = result.Labs.Keys.ToArray(); var parameters = Values("LabResultItems", "ParameterName");
        if (labIds.Length > 0 && parameters.Length > 0) foreach (var item in await db.LabResultItems.Where(item => EF.Parameter(labIds).Contains(item.LabResultId) && EF.Parameter(parameters).Contains(EF.Functions.Collate(item.ParameterName, Collation))).Select(item => new { item.LabResultId, item.ParameterName }).ToListAsync(token)) result.keys.Add("Item:" + item.LabResultId + ":" + item.ParameterName.TrimEnd());
        var rxIds = result.Prescriptions.Keys.ToArray();
        if (rxIds.Length > 0 && rows.Any(row => row.Template == "MedicationDoseSchedules")) foreach (var dose in await db.MedicationDoseSchedules.Where(dose => EF.Parameter(rxIds).Contains(dose.PrescriptionId)).Select(dose => new { dose.PrescriptionId, dose.DoseTime }).ToListAsync(token)) result.keys.Add("Dose:" + dose.PrescriptionId + ":" + dose.DoseTime.Ticks);
        var allergens = Values("PatientAllergies", "Allergen");
        if (ids.Length > 0 && allergens.Length > 0) foreach (var allergy in await db.PatientAllergies.Where(allergy => EF.Parameter(ids).Contains(allergy.PatientRecordId) && EF.Parameter(allergens).Contains(EF.Functions.Collate(allergy.Allergen, Collation))).Select(allergy => new { allergy.PatientRecordId, allergy.Allergen }).ToListAsync(token)) result.keys.Add("Allergy:" + allergy.PatientRecordId + ":" + allergy.Allergen.TrimEnd());
        // Existing parent stubs must be Unchanged before Add traverses a proposed child's navigation.
        db.AttachRange(result.Patients.Values); db.AttachRange(result.Encounters.Values);
        db.AttachRange(result.Labs.Values); db.AttachRange(result.Prescriptions.Values);
        return result;
    }
}
