using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Html.Parser;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public static class GuideContentCodec
{
    public const int MaximumContentLength = 100000;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static string Serialize(InstitutionalGuideContent content) => JsonSerializer.Serialize(content, Options);

    public static InstitutionalGuideContent Deserialize(string key, string json, int schemaVersion = InstitutionalGuideCatalog.SchemaVersion)
    {
        if (!InstitutionalGuideCatalog.All.ContainsKey(key) || schemaVersion != InstitutionalGuideCatalog.SchemaVersion)
            throw new ValidationException("The guide or content schema is not supported.");
        if (json.Length > MaximumContentLength) throw new ValidationException("Each translation must be at most 100,000 serialized characters.");
        InstitutionalGuideContent content;
        try { content = JsonSerializer.Deserialize<InstitutionalGuideContent>(json, Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException("The guide content is malformed."); }
        ValidateGraph(content);
        ValidateStructure(key, content);
        // Defense in depth for imported/manually altered SQL values, as well as editor input.
        foreach (var section in content.Sections)
        {
            foreach (var item in section.Items)
            {
                item.BodyHtml = AdvisoryHtml.Clean(item.BodyHtml);
                item.SupplementalHtml = AdvisoryHtml.Clean(item.SupplementalHtml);
            }
            section.Paragraphs = section.Paragraphs.Select(AdvisoryHtml.Clean).ToList();
        }
        ValidateStructure(key, content);
        return content;
    }

    public static string SerializeAndClean(string key, InstitutionalGuideContent content)
    {
        var result = Serialize(Deserialize(key, Serialize(content)));
        if (result.Length > MaximumContentLength) throw new ValidationException("Each translation must be at most 100,000 serialized characters.");
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ValidationException(message);
    }
    private static void Text(string? value, string label) => Require(!string.IsNullOrWhiteSpace(value), label + " is required.");
    private static void RichText(string value, string label)
    {
        using var document = new HtmlParser().ParseDocument(AdvisoryHtml.Clean(value));
        Require(!string.IsNullOrWhiteSpace(document.Body?.TextContent), label + " is required.");
    }

    private static void ValidateGraph(object value)
    {
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var field = property.GetValue(value);
            if (field is null)
            {
                Require(property.Name is "Opd" or "Radiology" or "PreRegistration" or "Location" or "RequirementNote" or "Detail" or "VerificationNote",
                    "Guide collections and text fields cannot be null.");
                continue;
            }
            if (field is string text)
                Require(text.Length <= (property.Name is "BodyHtml" or "SupplementalHtml" ? 10000 : 2000), "A guide text field is too long.");
            else if (field is System.Collections.IList list)
            {
                Require(list.Count <= (property.Name == "Steps" ? 50 : 100), "The guide contains too many rows.");
                foreach (var item in list)
                {
                    Require(item is not null, "Guide rows cannot be null.");
                    if (item is string row) Require(row.Length <= (property.Name == "Paragraphs" ? 10000 : 2000), "A guide row is too long.");
                    else ValidateGraph(item!);
                }
            }
            else if (!property.PropertyType.IsValueType) ValidateGraph(field);
        }
    }

    private static void Keys(IEnumerable<string> actual, IEnumerable<string> expected)
    {
        var keys = actual.ToArray();
        Require(keys.Length == keys.Distinct(StringComparer.Ordinal).Count() && keys.Order().SequenceEqual(expected.Order()),
            "Keep the existing guide, facility and study identifiers.");
    }

    private static void ValidateStructure(string key, InstitutionalGuideContent c)
    {
        Text(c.PageTitle, "Page title"); Text(c.Heading, "Heading");
        Require((key == "opd") == (c.Opd is not null) && (key == "radiology-preparation") == (c.Radiology is not null), "The content does not match this guide.");
        if (key == "malasakit")
        {
            Keys(c.Tiles.Select(t => t.Key), ["dswd", "drmc-malasakit", "maifip", "philhealth"]);
            Text(c.SectionTitle, "Section heading");
            foreach (var tile in c.Tiles) { Text(tile.Title, "Service name"); Text(tile.ActionLabel, "Service link label"); }
        }
        else Require(c.Tiles.Count == 0, "Service tiles belong only to the Malasakit hub.");

        var sectionCount = key switch { "dswd" => 4, "drmc-malasakit" or "philhealth" => 1, "maifip" => 3, _ => 0 };
        Require(c.Sections.Count == sectionCount, "Keep the existing section structure for this guide.");
        foreach (var section in c.Sections) Text(section.Title, "Section title");
        if (key is "dswd" or "drmc-malasakit" or "philhealth" or "maifip")
        {
            var documents = key == "maifip" ? c.Sections.Take(1) : c.Sections;
            foreach (var section in documents)
            {
                Require(section.Items.Count > 0, "Include at least one requirement.");
                foreach (var item in section.Items)
                {
                    RichText(item.BodyHtml, "Requirement text");
                    if (key is "maifip" or "philhealth") Text(item.Title, "Document title");
                }
            }
        }
        if (key == "maifip")
        {
            Require(c.Sections[1].Paragraphs.Count > 0 && c.Sections[2].Rows.Count > 0, "Include quotation instructions and hospital destinations.");
            foreach (var paragraph in c.Sections[1].Paragraphs) RichText(paragraph, "Quotation instruction");
            foreach (var row in c.Sections[2].Rows) { Text(row.Hospital, "Hospital name"); Text(row.NextStep, "Next step"); }
            Text(c.Sections[2].FirstColumnHeading, "Hospital column heading");
            Text(c.Sections[2].SecondColumnHeading, "Next step column heading");
            Text(c.Sections[2].RegionLabel, "Table description");
        }
        if (c.Opd is { } opd)
        {
            Text(opd.ReminderTitle, "Reminder heading"); Text(opd.ReminderBody, "Reminder text");
            Keys(opd.Facilities.Select(f => f.Key), InstitutionalGuideCatalog.Facilities);
            foreach (var facility in opd.Facilities)
            {
                Text(facility.Name, "Facility name"); Text(facility.ShortName, "Short facility name");
                Require(facility.Steps.Count > 0, "Include at least one OPD step.");
                foreach (var step in facility.Steps.Concat(facility.PreRegistration is { } pre ? [pre] : []))
                {
                    Text(step.Title, "Step title"); Text(step.Detail, "Step instructions");
                    foreach (var branch in step.Branches) Text(branch, "Branch instruction");
                    foreach (var destination in step.Destinations) Text(destination, "Destination");
                }
            }
        }
        if (c.Radiology is { } radiology)
        {
            Text(radiology.IntroText, "Preparation introduction"); Text(radiology.ContactPrefix, "Contact instructions");
            Text(radiology.GeneralHeading, "General heading"); Text(radiology.SafetyHeading, "Safety heading"); Text(radiology.ModalityHeading, "Study heading");
            Keys(radiology.Guides.Select(g => g.Key), InstitutionalGuideCatalog.Modalities);
            Require(radiology.GeneralItems.Count > 0 && radiology.SafetyNotices.Count > 0, "Include general preparation and safety guidance.");
            foreach (var text in radiology.GeneralItems.Concat(radiology.SafetyNotices)) Text(text, "Preparation instruction");
            foreach (var study in radiology.Guides)
            {
                Text(study.Title, "Study title"); Require(study.Items.Count > 0, "Include at least one instruction for each study.");
                foreach (var text in study.Items) Text(text, "Study instruction");
            }
        }
    }
}

public static class InstitutionalGuideDefaults
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Documents = new(() =>
        InstitutionalGuideCatalog.Cultures.ToDictionary(culture => culture, culture =>
        {
            using var stream = typeof(InstitutionalGuideDefaults).Assembly.GetManifestResourceStream($"DrmcPatientPortal.Content.InstitutionalGuides.v1.{culture}.json")
                ?? throw new InvalidOperationException("Bundled institutional guides are unavailable.");
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        }));

    public static string Culture(string? culture) => culture?.Split('-')[0].ToLowerInvariant() is "fil" ? "fil" :
        culture?.Split('-')[0].ToLowerInvariant() is "ceb" ? "ceb" : "en";

    public static InstitutionalGuideContent Get(string key, string culture = "en") =>
        GuideContentCodec.Deserialize(key, Documents.Value[Culture(culture)][key]);
}

public sealed class InstitutionalGuideService(ApplicationDbContext db, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(ClinicalClock.WallTime(clock.GetUtcNow()));

    public Task<InstitutionalGuideRevision?> ActiveRevisionAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!InstitutionalGuideCatalog.All.ContainsKey(key)) throw new ArgumentException("Unknown institutional guide.", nameof(key));
        var today = Today;
        return SqlReadRetry.RunAsync(db, () => db.InstitutionalGuideRevisions.AsNoTracking()
            .Where(r => r.GuideKey == key && r.EffectiveOn <= today)
            .OrderByDescending(r => r.EffectiveOn).ThenByDescending(r => r.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken), cancellationToken);
    }

    public async Task<ResolvedInstitutionalGuide> GetAsync(string key, string? culture = null, CancellationToken cancellationToken = default)
    {
        culture = InstitutionalGuideDefaults.Culture(culture ?? CultureInfo.CurrentUICulture.Name);
        var revision = await ActiveRevisionAsync(key, cancellationToken);
        var json = revision?.ContentFor(culture);
        return json is null ? new(key, InstitutionalGuideDefaults.Get(key, culture)) :
            new(key, GuideContentCodec.Deserialize(key, json, revision!.SchemaVersion), revision.RevisionNumber, revision.EffectiveOn, revision.LastReviewedAtUtc);
    }
}
