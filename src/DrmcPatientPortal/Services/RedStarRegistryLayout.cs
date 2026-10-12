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

    public static RedStarRegistrySheet? Find(string? name) => Sheets.SingleOrDefault(sheet =>
        string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));

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
