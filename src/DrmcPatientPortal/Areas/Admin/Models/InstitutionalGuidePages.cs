using System.ComponentModel.DataAnnotations;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DrmcPatientPortal.Areas.Admin.Models;

public sealed class InstitutionalGuideInput
{
    public string? RowVersion { get; set; }
    public int? SourceRevisionNumber { get; set; }
    [Required, Display(Name = "Effective date")]
    public DateOnly? EffectiveOn { get; set; }
    // Content is validated and sanitized only for cultures explicitly enabled by the editor.
    [ValidateNever] public GuideLocaleInput English { get; set; } = new();
    [ValidateNever] public GuideLocaleInput Filipino { get; set; } = new();
    [ValidateNever] public GuideLocaleInput Cebuano { get; set; } = new();
    public GuideLocaleInput Locale(string culture) => culture switch { "fil" => Filipino, "ceb" => Cebuano, _ => English };
}

public sealed class GuideLocaleInput
{
    public bool Enabled { get; set; }
    public InstitutionalGuideContent Content { get; set; } = new();
}

public sealed record InstitutionalGuideEditPage(string Key, string Title, InstitutionalGuideInput Input,
    int? ActiveRevisionNumber, DateOnly Today, IReadOnlyList<InstitutionalGuideRevision> Scheduled);
public sealed record InstitutionalGuideListRow(string Key, string Title, InstitutionalGuideRevision? Active,
    IReadOnlyList<InstitutionalGuideRevision> Scheduled);
public sealed record InstitutionalGuideList(IReadOnlyList<InstitutionalGuideListRow> Rows, DateOnly Today);
public sealed record InstitutionalGuideHistory(string Key, string Title, int? ActiveRevisionNumber,
    DateOnly Today, IReadOnlyList<InstitutionalGuideRevision> Revisions);
public sealed record InstitutionalGuideRevisionPage(string Key, string Title, InstitutionalGuideRevision Revision,
    IReadOnlyDictionary<string, InstitutionalGuideContent> Content);

// Explicit name prefixes keep nested collection binding stable when rows are reordered.
public sealed record GuideContentFields(string Prefix, string Key, InstitutionalGuideContent Content);
public sealed record GuideSectionFields(string Prefix, GuideSection Section, string Kind = "documents",
    bool DocumentTitles = true, bool Supplemental = false);
public sealed record GuideDocumentFields(string Prefix, GuideDocumentItem Item, bool ShowTitle = true, bool Supplemental = false);
public sealed record GuideStepFields(string Prefix, OpdStepContent Step);
public sealed record GuideStepListFields(string Prefix, IReadOnlyList<OpdStepContent> Steps);
public sealed record GuideStringFields(string Prefix, string Label, IReadOnlyList<string> Items, string Token = "__text__");
