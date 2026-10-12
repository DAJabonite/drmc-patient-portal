namespace DrmcPatientPortal.Services;

// Header definitions only; the staff workbook and its patient rows are never bundled.
internal static class RedStarStaffWorkbook
{
    private static readonly IReadOnlyDictionary<string, string[]> Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["LIVING"] =
        [
            "", "LEGEND", "NAME", "Testing Facility (Confirmed +)", "Age during Enrollment",
            "Current Age", "Address*", "HRN", "Patient Code", "Code Name",
            "Sex", "Category", "Perceived Sexuality", "Confirmatory Code", "UIC",
            "PHIC No.", "TB Status", "Tx Started", "Tx Completed", "Enrolment/Baseline Date",
            "ART Start Date @ RED STAR", "REFILL DATE", "Next Refill Date", "Last Contact No. Provided", "REGIMEN",
            "", "", "Latest CD4", "", "VIRAL LOAD",
            "", "TREATMENT STATUS", "OI", "OI Treatment Status", "DATE OF COMPLETION",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["REACTIVE WHEN TESTED, NONREACTI"] =
        [
            "No.", "LEGEND", "NAME", "Testing Facility (Confirmed +)", "Current Address",
            "HRN", "Patient Code", "Code Name", "Sex", "Perceived Sexuality",
            "Category", "SACCL Code", "UIC", "PHIC No.", "TB Status",
            "Tx Started", "Tx Completed", "Date Enrolled to RS-HACT", "ART Start Date @ RED STAR", "Last Visit @ RED STAR",
            "ARV Refill Date", "Last Contact No. Provided", "ARV Regimen", "", "",
            "Latest Viral Load (Date/Result)", "", "STATUS", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["TRANS OUT"] =
        [
            "", "LEG", "Patient Name", "Testing Facility (Confirmed +)", "Age during enrollment",
            "Current Address", "HRN", "Patient Code", "Code Name", "Sex",
            "Perceived Sexuality", "Category", "SACCL Code", "UIC", "PHIC No.",
            "TB Status", "Tx Started", "Tx Completed", "Date Enrolled @ RED STAR", "ART Start Date @ RED STAR",
            "Last Visit @ RED STAR", "ARV Refill Date", "Last Contact No. Provided", "ARV Regimen", "",
            "Date of Last Viral Load", "Result of Viral Load", "TRANSOUT DATE", "Name of Hub*", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ],
        ["PrEP"] =
        [
            "", "", "Patient Name", "Testing Facility", "Current Address",
            "Age", "Patient Code", "Sex", "Perceived Sexuality", "Category",
            "UIC", "PHIC No.", "PrEP Start Date @ RED STAR", "Last Visit @ RED STAR", "PrEP NEXT Refill Date",
            "Last Contact No. Provided", "PrEP Regimen", "Date of last HIV Test", "Result HIV Test", "Date of last HBsAg",
            "Result of HBsAg", "Date of Last Creatinine", "Result of Creatinine", "Date of Last RPR/Syphilis Test", "Result of RPR/Syphilis",
            "STATUS"
        ],
        ["NB PROPHYLAXIS"] =
        [
            "No.", "LEG", "Patient Name", "Testing Facility", "Hospital No.",
            "Patient Code", "Code Name", "SEX", "Perceived Sexuality", "Category",
            "SACCL Code", "UIC", "PHIC No.", "First Consult @ RED STAR", "ART Start Date",
            "Last Visit at RED STAR", "REFILL DATE", "NEXT REFILL DATE", "ARV Regimen", "STATUS",
            "", "", "", "", "",
            "", "", ""
        ],
        ["PEP"] =
        [
            "No.", "LEG", "Patient Name", "Testing Facility", "Hospital No.",
            "Patient Code", "Code Name", "SEX", "Perceived Sexuality", "Category",
            "SACCL Code", "UIC", "PHIC No.", "First Consult @ RED STAR", "ART Start Date",
            "Last Visit at RED STAR", "ARV Regimen", "STATUS", "REMARKS", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            ""
        ],
        ["RHIVDA 2025"] =
        [
            "NAME"
        ],
        ["EXPIRED"] =
        [
            "", "LEG", "Patient Name", "Testing Facility Confirmed +", "Age during enrollment",
            "Last Address Resided", "HRN", "Patient Code", "Code Name", "SEX",
            "Perceived Sexuality", "Category", "SACCL Code", "UIC", "PHIC No.",
            "Lung Status", "Tx Started", "Tx Completed", "Date Enrolled to HACT", "ART Start Date @ RED STAR",
            "Last Visit @ RED STAR", "ARV REGIMEN", "", "Latest Viral Load Count (Date/Result)", "",
            "LOCATION of Expired", "DATE EXPIRED", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", ""
        ],
    };

    public static IReadOnlyList<string> ForSheet(string name) => Headers[name];
}
