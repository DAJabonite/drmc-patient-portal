using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using DrmcPatientPortal.Areas.Admin.Models;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed record ImportTemplate(string Name, Type InputType, string[] Columns, bool Clinical)
{
    public string Sheet => Name + "_v1";
}

public static class ImportTemplates
{
    public const string ValidationVersion = "1.1.0";
    private static string[] Columns(string fields, bool clinical = false, string context = "") =>
        (clinical ? new[] { "SourcePatientKey", "SourcePatientName", "SourceBirthDate" } : Array.Empty<string>())
        .Concat(context.Split(',', StringSplitOptions.RemoveEmptyEntries)).Concat(fields.Split(',')).ToArray();
    public static readonly IReadOnlyList<ImportTemplate> All =
    [
        new("Patients", typeof(PatientInput), Columns("FullName,DateOfBirth,HospitalNumber", true), true),
        new("Doctors", typeof(DoctorInput), Columns("FullName,Title,Department,SubSpecialty,ClinicRoom,ScheduleSummary,OffersTeleconsult,Biography,PrcLicenseMasked,IsActive"), false),
        new("PublicAdvisories", typeof(PublicAdvisoryInput), Columns("Title,Slug,Category,Priority,Summary,ContentHtml,ImageUrl,IssuingUnit,PublishedAt,EffectiveUntil,IsPinned"), false),
        new("ClinicalEncounters", typeof(EncounterInput), Columns("EncounterReference,EncounterDate,Department,Type,HistoricalDoctorName,ChiefComplaint,PrimaryDiagnosis,SecondaryDiagnosis,ClinicalSummary,CarePlanAndInstructions,VitalSignsRecorded,FollowUpDate,FollowUpNotes", true, "SourceRecordKey"), true),
        new("LabResults", typeof(LabInput), Columns("AccessionNumber,TestName,Category,CollectedAt,ReleasedAt,Status,HistoricalDoctorName,Pathologist.HistoricalDoctorName,ResultSummary,PerformingUnit,ClinicalNotes", true, "SourceRecordKey,SourceEncounterKey"), true),
        new("LabResultItems", typeof(LabItemInput), Columns("ParameterName,Value,Unit,ReferenceRange,Flag", true, "SourceLabKey"), true),
        new("Prescriptions", typeof(PrescriptionInput), Columns("RxNumber,GenericName,BrandName,Dosage,DosageForm,Frequency,Instructions,Department,PrescribedAt,ValidUntil,LastRefillDate,RefillsTotal,RefillsRemaining,Status,HistoricalDoctorName", true, "SourceRecordKey"), true),
        new("MedicationDoseSchedules", typeof(DoseInput), Columns("DoseTime,DisplayOrder", true, "SourcePrescriptionKey"), true),
        new("PatientAllergies", typeof(AllergyInput), Columns("Allergen,Reaction,Severity,RecordedAt", true), true)
    ];
    public static ImportTemplate Get(string name) => All.SingleOrDefault(t => t.Name == name || t.Sheet == name)
        ?? throw new ImportRejectedException("Select a supported version 1 template.");

    public static object Input(ImportRow row, List<string> errors)
    {
        var template = Get(row.Template);
        var input = Activator.CreateInstance(template.InputType)!;
        foreach (var column in template.Columns.Where(c => !c.StartsWith("Source", StringComparison.Ordinal)))
        {
            var owner = input;
            var parts = column.Split('.');
            if (parts.Length == 2) owner = template.InputType.GetProperty(parts[0])!.GetValue(input)!;
            var property = owner.GetType().GetProperty(parts[^1]) ?? throw new InvalidOperationException("Template field is not an input property.");
            var text = row.Fields[column];
            try { property.SetValue(owner, ConvertValue(text, property)); }
            catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
            { errors.Add(column + ": enter a valid value."); }
        }
        Validate(input, errors);
        return input;
    }

    private static object? ConvertValue(string text, PropertyInfo property)
    {
        var nullable = Nullable.GetUnderlyingType(property.PropertyType);
        var type = nullable ?? property.PropertyType;
        if (type == typeof(string)) return text;
        if (string.IsNullOrWhiteSpace(text)) return nullable is not null ? null : type == typeof(bool) ? false : throw new FormatException();
        if (type == typeof(bool)) return bool.Parse(text);
        if (type == typeof(int)) return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (type.IsEnum) return Enum.Parse(type, text, false);
        if (type == typeof(DateTime))
        {
            if (!DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new FormatException();
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        }
        throw new FormatException();
    }

    public static void Validate(object input, List<string> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, true);
        errors.AddRange(results.Select(result => string.Join(", ", result.MemberNames) + ": invalid input."));
        if (input is LabInput lab) Validate(lab.Pathologist, errors);
    }
}

public sealed class ImportRejectedException(string message) : Exception(message);
