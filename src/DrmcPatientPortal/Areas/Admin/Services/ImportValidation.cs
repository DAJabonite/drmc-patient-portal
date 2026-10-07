using System.Globalization;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed record ImportNode(string Template, object Entity, PatientRecord? Patient);
public sealed record ImportPlan(ImportReport Report, List<ImportNode> Nodes);

public static class ImportValidation
{
    public static List<ImportGroup> Groups(IEnumerable<ImportRow> rows) => rows.Where(row => ImportTemplates.Get(row.Template).Clinical)
        .GroupBy(row => row.Fields["SourcePatientKey"].Trim(), StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group =>
        {
            var source = group.FirstOrDefault(row => row.Template == "Patients") ?? group.First();
            return new ImportGroup(group.Key, source.Fields.GetValueOrDefault("FullName") ?? source.Fields["SourcePatientName"],
                source.Fields.GetValueOrDefault("DateOfBirth") ?? source.Fields["SourceBirthDate"], group.Count(), source.Template == "Patients", source.Fields.GetValueOrDefault("HospitalNumber") ?? "");
        }).ToList();

    public static async Task<ImportPlan> BuildAsync(ApplicationDbContext db, ImportEnvelope envelope, CancellationToken token)
    {
        var detect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try { return await BuildCoreAsync(db, envelope, token); }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detect; }
    }

    private static async Task<ImportPlan> BuildCoreAsync(ApplicationDbContext db, ImportEnvelope envelope, CancellationToken token)
    {
        var rows = ImportParser.Parse(envelope.Content, envelope.Format, envelope.Template);
        var lookups = await ImportLookups.LoadAsync(db, rows, envelope, token);
        var reviews = rows.ToDictionary(row => row, row => new List<string>());
        var nodes = new List<ImportNode>(); var nextId = -1;
        var patients = new Dictionary<string, PatientRecord>(StringComparer.Ordinal);
        var sourceParents = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var (field, value) in row.Fields.Where(pair => pair.Key.StartsWith("Source", StringComparison.Ordinal)))
                if (value.Length > (field is "SourcePatientName" ? 100000 : field is "SourcePatientKey" ? 200 : 450)) reviews[row].Add(field + ": too long.");
            if (ImportTemplates.Get(row.Template).Clinical && string.IsNullOrWhiteSpace(row.Fields["SourcePatientKey"])) reviews[row].Add("SourcePatientKey is required; staff must reconcile this row.");
            if (row.Fields.ContainsKey("SourceRecordKey") && string.IsNullOrWhiteSpace(row.Fields["SourceRecordKey"])) reviews[row].Add("SourceRecordKey is required.");
        }
        var groups = rows.Where(row => ImportTemplates.Get(row.Template).Clinical).GroupBy(row => row.Fields["SourcePatientKey"].Trim(), StringComparer.Ordinal).ToArray();
        var groupKeys = groups.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var mappingGroups = envelope.Mappings.GroupBy(mapping => mapping.SourcePatientKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var errors = new List<string>(); var mappings = mappingGroups.GetValueOrDefault(group.Key) ?? [];
            if (group.Key.Length == 0 || mappings.Length != 1) errors.Add("Choose exactly one explicit patient mapping for this source group.");
            else
            {
                var mapping = mappings[0]; var registry = group.Where(row => row.Template == "Patients").ToArray();
                if (registry.Length > 1) errors.Add("Duplicate patient template rows for this group block the batch.");
                if (mapping.CreateNew == mapping.PatientRecordId.HasValue) errors.Add("Select an existing record or confirm creation of a new record, not both.");
                else if (mapping.CreateNew)
                {
                    var input = registry.Length == 1 ? (PatientInput)ImportTemplates.Input(registry[0], errors) : mapping.Patient;
                    ImportTemplates.Validate(input, errors);
                    var patient = new PatientRecord { Id = nextId-- };
                    if (errors.Count == 0)
                    {
                        var result = await new PatientEditor().ApplyAsync(db, input, patient, null, true, token, lookups);
                        if (!result.Succeeded) errors.Add(result.Error!);
                        if (patient.HospitalNumber is not null && !duplicates.Add("Hospital:" + patient.HospitalNumber)) errors.Add("Duplicate hospital numbers in this batch block execution.");
                    }
                    if (errors.Count == 0) { db.Add(patient); patients[group.Key] = patient; nodes.Add(new("Patients", patient, patient)); }
                }
                else
                {
                    if (registry.Length != 0) errors.Add("Patient template rows only create new records; existing records cannot be updated or skipped.");
                    var patient = mapping.PatientRecordId is { } patientId ? lookups.Patients.GetValueOrDefault(patientId) : null;
                    if (patient is null) errors.Add("The selected patient record no longer exists.");
                    else if (errors.Count == 0) patients[group.Key] = patient;
                }
            }
            foreach (var row in group) reviews[row].AddRange(errors);
        }
        if (envelope.Mappings.Any(mapping => !groupKeys.Contains(mapping.SourcePatientKey)))
            foreach (var row in rows) reviews[row].Add("A mapping refers to a source group not present in this file.");

        T? Parent<T>(ImportRow row, string field, string entity) where T : class
        {
            var key = row.Fields[field].Trim();
            if (key.Length == 0 && field == "SourceEncounterKey") return null;
            T? parent = null;
            if (key.StartsWith("batch:", StringComparison.Ordinal) && sourceParents.TryGetValue(entity + ":" + key[6..], out var staged)) parent = staged as T;
            else if (key.StartsWith("existing:", StringComparison.Ordinal))
            {
                var matches = lookups.References.GetValueOrDefault(entity + ":" + key[9..].TrimEnd());
                if (matches?.Count == 1) parent = matches[0] as T;
            }
            if (parent is null) reviews[row].Add(field + ": choose one existing parent reference or a parent key within this batch.");
            return parent;
        }
        async Task Add<TEntity, TInput>(ImportRow row, TInput input, AdminEntity<TEntity, TInput> editor, OwnershipContext? context, PatientRecord? patient)
            where TEntity : class, new() where TInput : class, IAdminInput, new()
        {
            if (reviews[row].Count != 0) return;
            var entity = new TEntity(); db.Entry(entity).Property("Id").CurrentValue = nextId--;
            var result = await editor.ApplyAsync(db, input, entity, context, true, token, lookups);
            if (!result.Succeeded) { reviews[row].Add(result.Error!); return; }
            string? key = entity switch
            {
                ClinicalEncounter encounter => "Encounter:" + encounter.EncounterReference,
                LabResult lab => "Lab:" + lab.AccessionNumber,
                Prescription rx => "Rx:" + rx.RxNumber,
                LabResultItem item => "Item:" + item.LabResultId + ":" + item.ParameterName,
                MedicationDoseSchedule dose => "Dose:" + dose.PrescriptionId + ":" + dose.DoseTime.Ticks,
                PatientAllergy allergy => "Allergy:" + allergy.PatientRecordId + ":" + allergy.Allergen,
                PublicAdvisory advisory => "Slug:" + advisory.Slug,
                _ => null
            };
            if (key is not null && !duplicates.Add(key)) { reviews[row].Add("Duplicate records in this batch block execution."); return; }
            if (key is not null && lookups.Exists(key))
            { reviews[row].Add("An existing duplicate blocks this batch; imports do not update, skip or merge records."); return; }
            if (row.Fields.TryGetValue("SourceRecordKey", out var sourceKey) && !sourceParents.TryAdd(row.Template + ":" + sourceKey.Trim(), entity))
            { reviews[row].Add("Duplicate source parent keys block this batch."); return; }
            db.Add(entity); lookups.Track(entity); nodes.Add(new(row.Template, entity, patient));
        }
        foreach (var row in rows.OrderBy(row => Array.IndexOf(new[] { "Patients", "Doctors", "PublicAdvisories", "ClinicalEncounters", "LabResults", "Prescriptions", "LabResultItems", "MedicationDoseSchedules", "PatientAllergies" }, row.Template)))
        {
            if (reviews[row].Count != 0 || row.Template == "Patients") continue;
            var input = ImportTemplates.Input(row, reviews[row]);
            if (reviews[row].Count != 0) continue;
            patients.TryGetValue(row.Fields.GetValueOrDefault("SourcePatientKey")?.Trim() ?? "", out var patient);
            var context = patient is null ? null : new OwnershipContext(patient, "Import patient", patient.Id);
            switch (input)
            {
                case DoctorInput doctorInput:
                    var doctor = new Doctor { Id = nextId-- }; doctorInput.Apply(doctor);
                    if (!duplicates.Add("Doctor:" + doctor.Department + ":" + doctor.FullName) || lookups.Exists("Doctor:" + doctor.Department + ":" + doctor.FullName)) reviews[row].Add("An existing or staged duplicate doctor blocks this batch.");
                    else { db.Add(doctor); nodes.Add(new("Doctors", doctor, null)); }
                    break;
                case PublicAdvisoryInput advisory: await Add(row, advisory, new AdvisoryEditor(), null, null); break;
                case EncounterInput encounter: await Add(row, encounter, new EncounterEditor(), context, patient); break;
                case LabInput lab:
                    var encounterParent = Parent<ClinicalEncounter>(row, "SourceEncounterKey", "ClinicalEncounters");
                    if (encounterParent is not null)
                    {
                        if (encounterParent.PatientRecordId != patient?.Id) reviews[row].Add("The encounter belongs to another patient.");
                        lab.ClinicalEncounterId = encounterParent.Id;
                    }
                    await Add(row, lab, new LabEditor(), context, patient); break;
                case PrescriptionInput prescription: await Add(row, prescription, new PrescriptionEditor(), context, patient); break;
                case LabItemInput item:
                    var labParent = Parent<LabResult>(row, "SourceLabKey", "LabResults");
                    if (labParent is not null && labParent.PatientRecordId != patient?.Id) reviews[row].Add("The lab belongs to another patient.");
                    await Add(row, item, new LabItemEditor(), labParent is null || patient is null ? null : new(patient, "Import lab", labParent.Id), patient); break;
                case DoseInput dose:
                    var prescriptionParent = Parent<Prescription>(row, "SourcePrescriptionKey", "Prescriptions");
                    if (prescriptionParent is not null && prescriptionParent.PatientRecordId != patient?.Id) reviews[row].Add("The prescription belongs to another patient.");
                    await Add(row, dose, new DoseEditor(), prescriptionParent is null || patient is null ? null : new(patient, "Import prescription", prescriptionParent.Id), patient); break;
                case AllergyInput allergy: await Add(row, allergy, new AllergyEditor(), context, patient); break;
            }
        }
        var report = new ImportReport(rows.Select(row => new ImportRowReview(row.Template, row.Number, row.Fields.GetValueOrDefault("SourcePatientKey") ?? "",
            row.Fields.GetValueOrDefault("SourceRecordKey") ?? row.Fields.GetValueOrDefault("Slug") ?? "",
            !ImportTemplates.Get(row.Template).Clinical ? "Create record" : patients.TryGetValue(row.Fields.GetValueOrDefault("SourcePatientKey")?.Trim() ?? "", out var mapped) ? mapped.Id < 0 ? "Create new record" : "Existing patient #" + mapped.Id : "Unmapped",
            reviews[row].Distinct().ToArray())).ToList(),
            nodes.GroupBy(node => node.Template).ToDictionary(group => group.Key, group => group.Count()), ImportStaging.ApprovalHash(envelope));
        return new(report, nodes);
    }
}
