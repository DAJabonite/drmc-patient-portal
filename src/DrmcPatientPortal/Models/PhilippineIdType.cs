namespace DrmcPatientPortal.Models;

// Static reference data for accepted Philippine Government Identification Documents,
// verified against PSA/PhilSys, LTO, DFA, GSIS/SSS, PhilHealth, PRC, and PHLPost official standards.
public record PhilippineIdType(
    string Name,
    string Category,
    bool SupportsName,
    bool SupportsDob,
    bool SupportsAddress,
    bool SupportsSex,
    bool SupportsBloodType,
    bool NeedsBackPhoto,
    string FramingGuidance,
    string? Notes = null
);

public static class PhilippineIdTypes
{
    public const string PhilSys = "Philippine National ID (PhilSys)";
    public const string DriversLicense = "Driver's License";
    public const string Passport = "Philippine Passport";
    public const string Umid = "UMID";
    public const string PostalId = "Postal ID";
    public const string PhilHealth = "PhilHealth ID";
    public const string SssGsis = "SSS / GSIS ID";
    public const string PrcId = "PRC ID";

    public static readonly IReadOnlyList<PhilippineIdType> All = new List<PhilippineIdType>
    {
        new(
            PhilSys,
            "National Identification",
            SupportsName: true,
            SupportsDob: true,
            SupportsAddress: true,
            SupportsSex: true,
            SupportsBloodType: true,
            NeedsBackPhoto: true,
            FramingGuidance: "Position the front of your PhilSys National ID card within the frame. You will also be prompted for the back of the card.",
            Notes: "Sex and blood type are printed on the back of the card."
        ),
        new(
            DriversLicense,
            "Land Transportation Office (LTO)",
            SupportsName: true,
            SupportsDob: true,
            SupportsAddress: true,
            SupportsSex: true,
            SupportsBloodType: true,
            NeedsBackPhoto: false,
            FramingGuidance: "Position the front of your LTO Driver's License card within the frame, ensuring all printed text is glare-free and legible.",
            Notes: "Contains comprehensive demographic and blood type details."
        ),
        new(
            Passport,
            "Department of Foreign Affairs (DFA)",
            SupportsName: true,
            SupportsDob: true,
            SupportsAddress: false,
            SupportsSex: true,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position the passport biodata page within the frame, ensuring the 2-line Machine Readable Zone (MRZ) at the bottom is clear and uncropped.",
            Notes: "Standardized 2-line ICAO 9303 MRZ format for high-reliability parsing."
        ),
        new(
            Umid,
            "Unified Multi-Purpose ID",
            SupportsName: true,
            SupportsDob: true,
            SupportsAddress: true,
            SupportsSex: true,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position the front of your UMID card within the frame, ensuring the CRN and full name are visible.",
            Notes: "Front-only extraction carrying CRN, name, birth date, address, and sex."
        ),
        new(
            PostalId,
            "PHLPost Digital Postal ID",
            SupportsName: true,
            SupportsDob: true,
            SupportsAddress: true,
            SupportsSex: false,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position the front of your Postal ID card within the frame, keeping all details sharply in focus.",
            Notes: "Extracts full name, birth date, address, and Postal ID reference number."
        ),
        new(
            PhilHealth,
            "Philippine Health Insurance Corporation",
            SupportsName: true,
            SupportsDob: false,
            SupportsAddress: false,
            SupportsSex: false,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position your PhilHealth Identification Card (PIC) within the frame, ensuring your 12-digit PhilHealth number and name are visible.",
            Notes: "Name and 12-digit PhilHealth Identification Number (PIN) extraction."
        ),
        new(
            SssGsis,
            "Social Security System / GSIS",
            SupportsName: true,
            SupportsDob: false,
            SupportsAddress: false,
            SupportsSex: false,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position your SSS or GSIS member card within the frame, ensuring the card number and member name are clearly visible.",
            Notes: "Name and member identification number extraction."
        ),
        new(
            PrcId,
            "Professional Regulation Commission",
            SupportsName: true,
            SupportsDob: false,
            SupportsAddress: false,
            SupportsSex: false,
            SupportsBloodType: false,
            NeedsBackPhoto: false,
            FramingGuidance: "Position your PRC Professional Identification Card within the frame, ensuring your registration number and full name are legible.",
            Notes: "Name and PRC registration number extraction."
        )
    };

    public static PhilippineIdType? GetByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return All.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresBackPhoto(string? idType)
    {
        var type = GetByName(idType);
        return type?.NeedsBackPhoto ?? false;
    }
}
