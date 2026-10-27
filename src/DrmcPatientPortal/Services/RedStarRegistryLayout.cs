using System.Text;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Services;

public sealed record RedStarRegistryColumn(string Field, string Header);
public sealed record RedStarRegistrySheet(string Name, IReadOnlyList<RedStarRegistryColumn> Columns);

// Supported registry fields follow the clinic's worksheet order. Other saved portal
// fields are appended so a core export can be imported again without losing values.
public static class RedStarRegistryLayout
{
    private static readonly RedStarRegistryColumn[] Core =
    [
        new("FullName", "NAME"), new("Address", "Address*"), new("HospitalNumber", "HRN"),
        new("PatientCode", "Patient Code"), new("CodeName", "Code Name"),
        new("EnrolledDate", "Enrolment/Baseline Date"), new("StartDate", "ART Start Date @ RED STAR"),
        new("DispensedDate", "REFILL DATE"), new("NextRefillDate", "Next Refill Date"),
        new("ContactNumber", "Last Contact No. Provided"), new("Medicines", "REGIMEN"),
        new("Status", "TREATMENT STATUS"), new("Program", "Program"),
        new("StatusDate", "Status Date"), new("ReceivingHub", "Receiving Hub")
    ];

    public static readonly IReadOnlyList<RedStarRegistryColumn> ImportColumns = Core
        .Select(column => column.Field == "Status" ? column with { Header = "Status" } : column).ToArray();

    public static byte[] CsvTemplate()
    {
        Dictionary<string, string>[] samples =
        [
            new()
            {
                ["FullName"] = "Synthetic Patient Alpha", ["Address"] = "Sample Street, Fictional City",
                ["HospitalNumber"] = "0000123", ["PatientCode"] = "SAMPLE-001", ["CodeName"] = "ALPHA",
                ["EnrolledDate"] = "2026-01-10", ["StartDate"] = "2026-01-12",
                ["DispensedDate"] = "2026-10-01", ["NextRefillDate"] = "2026-10-31",
                ["ContactNumber"] = "+639171234567", ["Medicines"] = "Sample medicine (example only)",
                ["Status"] = "Active", ["Program"] = "ART", ["StatusDate"] = "", ["ReceivingHub"] = ""
            },
            new()
            {
                ["FullName"] = "Synthetic Patient Niño", ["Address"] = "Sample \"B\" Street, Fictional City",
                ["HospitalNumber"] = "0000456", ["PatientCode"] = "SAMPLE-002", ["CodeName"] = "BETA",
                ["EnrolledDate"] = "2025-05-01", ["StartDate"] = "2025-05-02",
                ["DispensedDate"] = "", ["NextRefillDate"] = "", ["ContactNumber"] = "",
                ["Medicines"] = "Sample medicine (example only)", ["Status"] = "Expired", ["Program"] = "ART",
                ["StatusDate"] = "2026-10-15", ["ReceivingHub"] = ""
            }
        ];
        static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder();
        csv.AppendJoin(',', ImportColumns.Select(column => Escape(column.Header))).Append("\r\n");
        foreach (var sample in samples)
            csv.AppendJoin(',', ImportColumns.Select(column => Escape(sample[column.Field]))).Append("\r\n");
        var encoding = new UTF8Encoding(true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray();
    }

    private static RedStarRegistrySheet Sheet(string name, params RedStarRegistryColumn[] staffColumns)
    {
        var fields = staffColumns.Select(column => column.Field).ToHashSet(StringComparer.Ordinal);
        return new(name, staffColumns.Concat(Core.Where(column => !fields.Contains(column.Field))).ToArray());
    }

    public static readonly IReadOnlyList<RedStarRegistrySheet> Sheets =
    [
        new("LIVING", Core),
        Sheet("REACTIVE WHEN TESTED, NONREACTI",
            new("FullName", "NAME"), new("Address", "Current Address"), new("HospitalNumber", "HRN"),
            new("PatientCode", "Patient Code"), new("CodeName", "Code Name"),
            new("EnrolledDate", "Date Enrolled to RS-HACT"), new("StartDate", "ART Start Date @ RED STAR"),
            new("DispensedDate", "ARV Refill Date"), new("ContactNumber", "Last Contact No. Provided"),
            new("Medicines", "ARV Regimen"), new("Status", "STATUS")),
        Sheet("TRANS OUT",
            new("FullName", "Patient Name"), new("Address", "Current Address"), new("HospitalNumber", "HRN"),
            new("PatientCode", "Patient Code"), new("CodeName", "Code Name"),
            new("EnrolledDate", "Date Enrolled @ RED STAR"), new("StartDate", "ART Start Date @ RED STAR"),
            new("DispensedDate", "ARV Refill Date"), new("ContactNumber", "Last Contact No. Provided"),
            new("Medicines", "ARV Regimen"), new("StatusDate", "TRANSOUT DATE"), new("ReceivingHub", "Name of Hub*")),
        Sheet("PrEP",
            new("FullName", "Patient Name"), new("Address", "Current Address"), new("PatientCode", "Patient Code"),
            new("StartDate", "PrEP Start Date @ RED STAR"), new("NextRefillDate", "PrEP NEXT Refill Date"),
            new("ContactNumber", "Last Contact No. Provided"), new("Medicines", "PrEP Regimen"), new("Status", "STATUS")),
        Sheet("NB PROPHYLAXIS",
            new("FullName", "Patient Name"), new("HospitalNumber", "Hospital No."), new("PatientCode", "Patient Code"),
            new("CodeName", "Code Name"), new("StartDate", "ART Start Date"), new("DispensedDate", "REFILL DATE"),
            new("NextRefillDate", "NEXT REFILL DATE"), new("Medicines", "ARV Regimen"), new("Status", "STATUS")),
        Sheet("PEP",
            new("FullName", "Patient Name"), new("HospitalNumber", "Hospital No."), new("PatientCode", "Patient Code"),
            new("CodeName", "Code Name"), new("StartDate", "ART Start Date"), new("Medicines", "ARV Regimen"), new("Status", "STATUS")),
        Sheet("RHIVDA 2025", new RedStarRegistryColumn("FullName", "NAME")),
        Sheet("EXPIRED",
            new("FullName", "Patient Name"), new("Address", "Last Address Resided"), new("HospitalNumber", "HRN"),
            new("PatientCode", "Patient Code"), new("CodeName", "Code Name"),
            new("EnrolledDate", "Date Enrolled to HACT"), new("StartDate", "ART Start Date @ RED STAR"),
            new("Medicines", "ARV REGIMEN"), new("StatusDate", "DATE EXPIRED"))
    ];

    public static RedStarRegistrySheet? Find(string? name)
    {
        var normalized = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        if (normalized.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)) normalized = "LIVING";
        return Sheets.SingleOrDefault(sheet => string.Equals(sheet.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<RedStarRegistryColumn> ExportColumns(RedStarRegistrySheet sheet)
    {
        var staffColumns = RedStarStaffWorkbook.ForSheet(sheet.Name).Select(header =>
            new RedStarRegistryColumn(sheet.Columns.SingleOrDefault(column =>
                string.Equals(column.Header, header, StringComparison.OrdinalIgnoreCase))?.Field ?? "", header)).ToArray();
        var represented = staffColumns.Where(column => column.Field.Length != 0).Select(column => column.Field).ToHashSet(StringComparer.Ordinal);
        return staffColumns.Concat(sheet.Columns.Where(column => !represented.Contains(column.Field))).ToArray();
    }

    public static string SheetFor(RedStarEnrollment enrollment) => SheetFor(enrollment.Program, enrollment.Status);

    public static string SheetFor(RedStarProgram program, RedStarStatus status)
    {
        if (status == RedStarStatus.TransferredOut) return "TRANS OUT";
        if (status == RedStarStatus.Deceased) return "EXPIRED";
        return program switch
        {
            RedStarProgram.PrEP => "PrEP", RedStarProgram.PEP => "PEP",
            RedStarProgram.NewbornProphylaxis => "NB PROPHYLAXIS", RedStarProgram.Unverified => "RHIVDA 2025",
            _ => "LIVING"
        };
    }
}
