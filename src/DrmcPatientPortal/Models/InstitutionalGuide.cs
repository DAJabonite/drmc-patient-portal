namespace DrmcPatientPortal.Models;

public static class InstitutionalGuideCatalog
{
    public const int SchemaVersion = 1;
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["malasakit"] = "Malasakit hub", ["dswd"] = "DSWD requirements",
        ["drmc-malasakit"] = "DRMC Malasakit services", ["maifip"] = "DOH-MAIFIP requirements",
        ["philhealth"] = "PhilHealth requirements", ["opd"] = "OPD guide",
        ["radiology-preparation"] = "Radiology preparation"
    };
    public static readonly string[] Cultures = ["en", "fil", "ceb"];
    public static readonly string[] Facilities = ["main", "bucas", "ccm", "acc"];
    public static readonly string[] Modalities = ["xray", "ultrasound", "ct", "mri", "mammography"];
}

public sealed class InstitutionalGuideHead
{
    public string Key { get; set; } = "";
    public int LatestRevisionNumber { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class InstitutionalGuideRevision
{
    public long Id { get; set; }
    public string GuideKey { get; set; } = "";
    public int RevisionNumber { get; set; }
    public int SchemaVersion { get; set; } = InstitutionalGuideCatalog.SchemaVersion;
    public int? SourceRevisionNumber { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastReviewedAtUtc { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public string? EnglishJson { get; set; }
    public string? FilipinoJson { get; set; }
    public string? CebuanoJson { get; set; }

    public string? ContentFor(string culture) => culture switch { "fil" => FilipinoJson, "ceb" => CebuanoJson, _ => EnglishJson };
}

// Layout, URLs, icons and DOM identifiers belong to the catalogue and Razor templates.
// These mutable, typed documents are also the MVC editor's binding contract.
public sealed class InstitutionalGuideContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string PageTitle { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Heading { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Kicker { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Badge { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Breadcrumb { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ParentLabel { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string BackLabel { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string SectionTitle { get; set; } = "";
    public List<GuideTile> Tiles { get; set; } = [];
    public List<GuideSection> Sections { get; set; } = [];
    public OpdGuideContent? Opd { get; set; }
    public RadiologyGuideContent? Radiology { get; set; }
}

public sealed class GuideTile
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Key { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ActionLabel { get; set; } = "";
}

public sealed class GuideSection
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = "";
    public List<GuideDocumentItem> Items { get; set; } = [];
    public List<string> Paragraphs { get; set; } = [];
    public List<GuideDestinationRow> Rows { get; set; } = [];
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string FirstColumnHeading { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string SecondColumnHeading { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string RegionLabel { get; set; } = "";
}

public sealed class GuideDocumentItem
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string BodyHtml { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string SupplementalHtml { get; set; } = "";
}

public sealed class GuideDestinationRow
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Hospital { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string NextStep { get; set; } = "";
}

public sealed class OpdGuideContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ReminderTitle { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ReminderBody { get; set; } = "";
    public List<OpdFacilityContent> Facilities { get; set; } = [];
}

public sealed class OpdFacilityContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Key { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Name { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ShortName { get; set; } = "";
    public OpdStepContent? PreRegistration { get; set; }
    public List<OpdStepContent> Steps { get; set; } = [];
    public string? VerificationNote { get; set; }
    public OpdFacilityFlow ToFlow() => new(Key, Name, ShortName, PreRegistration?.ToStep(0),
        Steps.Select((step, index) => step.ToStep(index + 1)).ToArray(), VerificationNote);
}

public sealed class OpdStepContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string? RequirementNote { get; set; }
    public string? Detail { get; set; }
    public List<string> Branches { get; set; } = [];
    public bool IsConditional { get; set; }
    public List<string> Destinations { get; set; } = [];
    public OpdGuideStep ToStep(int number) => new(number, Title, Location, RequirementNote, Detail, Branches, IsConditional, Destinations);
}

public sealed class RadiologyGuideContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string IntroText { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ContactPrefix { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ContactSuffix { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string GeneralHeading { get; set; } = "";
    public List<string> GeneralItems { get; set; } = [];
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string SafetyHeading { get; set; } = "";
    public List<string> SafetyNotices { get; set; } = [];
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string ModalityHeading { get; set; } = "";
    public List<RadiologyStudyGuideContent> Guides { get; set; } = [];
}

public sealed class RadiologyStudyGuideContent
{
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Key { get; set; } = "";
    [System.ComponentModel.DataAnnotations.DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = "";
    public List<string> Items { get; set; } = [];
}

public sealed record ResolvedInstitutionalGuide(string Key, InstitutionalGuideContent Content,
    int? RevisionNumber = null, DateOnly? EffectiveOn = null, DateTime? LastReviewedAtUtc = null)
{
    public bool IsOverride => RevisionNumber.HasValue;
}

public sealed record RadiologyPreparationViewModel(ResolvedInstitutionalGuide Guide, ClinicalDepartment Department);
