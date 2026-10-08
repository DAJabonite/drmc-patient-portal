using System.Globalization;
using System.Text;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class ImportExamples
{
    public static IReadOnlyList<string[]> Rows(ImportTemplate template, bool workbook)
    {
        var rows = new List<string[]>();
        for (var i = 1; i <= 5; i++)
        {
            var number = i.ToString("000", CultureInfo.InvariantCulture);
            var name = "Fictional Patient Example " + number;
            var birth = "1990-01-0" + i;
            var doctor = "Fictional Doctor Example " + number;
            var values = new Dictionary<string, string>
            {
                ["SourcePatientKey"] = "P" + number, ["SourcePatientName"] = name,
                ["SourceBirthDate"] = birth, ["HistoricalDoctorName"] = doctor,
                ["Department"] = "Internal Medicine"
            };
            void Set(params (string Key, string Value)[] fields)
            { foreach (var (key, value) in fields) values[key] = value; }
            string Parent(string key, string reference) => workbook ? "batch:" + key + number : "existing:" + reference + number;
            switch (template.Name)
            {
                case "Patients":
                    Set(("FullName", name), ("DateOfBirth", birth), ("HospitalNumber", "EXAMPLE-" + number)); break;
                case "Doctors":
                    Set(("FullName", doctor), ("Title", "Fictional physician"), ("SubSpecialty", "Example specialty"),
                        ("ClinicRoom", "Example room " + number), ("ScheduleSummary", "Monday 08:00-12:00"),
                        ("OffersTeleconsult", "false"), ("Biography", "Fictional example only."),
                        ("PrcLicenseMasked", "EXAMPLE-***"), ("IsActive", "true")); break;
                case "PublicAdvisories":
                    Set(("Title", "Fictional Advisory Example " + number), ("Slug", "fictional-advisory-example-" + number),
                        ("Category", "Advisory"), ("Priority", "Normal"), ("Summary", "Fictional example only."),
                        ("ContentHtml", "<p>Replace this fictional example before importing real data.</p>"),
                        ("IssuingUnit", "Fictional example unit"), ("PublishedAt", "2026-01-10T08:00:00"),
                        ("EffectiveUntil", "2026-02-10T08:00:00"), ("IsPinned", "false")); break;
                case "ClinicalEncounters":
                    Set(("SourceRecordKey", "ENC" + number), ("EncounterReference", "EXAMPLE-ENC-" + number),
                        ("EncounterDate", "2026-01-10T08:00:00"), ("Type", "OpdConsultation"),
                        ("ChiefComplaint", "Fictional example complaint"), ("PrimaryDiagnosis", "Fictional example diagnosis"),
                        ("ClinicalSummary", "Fictional example only."), ("CarePlanAndInstructions", "Example instructions only."),
                        ("VitalSignsRecorded", "Example values only"), ("FollowUpDate", "2026-01-17T08:00:00"),
                        ("FollowUpNotes", "Fictional example follow-up")); break;
                case "LabResults":
                    Set(("SourceRecordKey", "LAB" + number), ("SourceEncounterKey", Parent("ENC", "EXAMPLE-ENC-")),
                        ("AccessionNumber", "EXAMPLE-LAB-" + number), ("TestName", "Fictional example blood count"),
                        ("Category", "Hematology"), ("CollectedAt", "2026-01-10T09:00:00"),
                        ("ReleasedAt", "2026-01-10T10:00:00"), ("Status", "Available"),
                        ("Pathologist.HistoricalDoctorName", "Fictional Pathologist Example " + number),
                        ("ResultSummary", "Fictional example only."), ("PerformingUnit", "Fictional example laboratory"),
                        ("ClinicalNotes", "Example data; replace before real import.")); break;
                case "LabResultItems":
                    Set(("SourceLabKey", Parent("LAB", "EXAMPLE-LAB-")), ("ParameterName", "Example Hemoglobin"),
                        ("Value", "14.2"), ("Unit", "g/dL"), ("ReferenceRange", "12.0-16.0"), ("Flag", "Normal")); break;
                case "Prescriptions":
                    Set(("SourceRecordKey", "RX" + number), ("RxNumber", "EXAMPLE-RX-" + number),
                        ("GenericName", "Fictional Example Medication"), ("BrandName", "Example brand"),
                        ("Dosage", "Example dose only"), ("DosageForm", "Tablet"), ("Frequency", "Once daily"),
                        ("Instructions", "Fictional example; not prescribing instructions."),
                        ("PrescribedAt", "2026-01-10T08:00:00"), ("ValidUntil", "2026-02-10T08:00:00"),
                        ("RefillsTotal", "1"), ("RefillsRemaining", "1"), ("Status", "Active")); break;
                case "MedicationDoseSchedules":
                    Set(("SourcePrescriptionKey", Parent("RX", "EXAMPLE-RX-")), ("DoseTime", "08:00"), ("DisplayOrder", "0")); break;
                case "PatientAllergies":
                    Set(("Allergen", "Fictional Example Allergen"), ("Reaction", "Fictional example reaction"),
                        ("Severity", "Mild"), ("RecordedAt", "2026-01-10T08:00:00")); break;
                default: throw new ImportRejectedException("Select a supported version 1 template.");
            }
            rows.Add(template.Columns.Select(column => values.GetValueOrDefault(column, "")).ToArray());
        }
        return rows;
    }

    public static byte[] Csv(ImportTemplate template)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var lines = new[] { string.Join(',', template.Columns) }
            .Concat(Rows(template, false).Select(row => string.Join(',', row.Select(Quote))));
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }
}
