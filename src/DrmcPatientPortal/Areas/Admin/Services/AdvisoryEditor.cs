using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Services;

public sealed class AdvisoryEditor : AdminEntity<PublicAdvisory, PublicAdvisoryInput>
{
    public override string Name => "PublicAdvisories";
    public override string Title => "Public advisories";
    public override string SearchColumn => nameof(PublicAdvisory.Title);
    public override string Summary(PublicAdvisory advisory) => advisory.Slug;
    public override PublicAdvisoryInput Input(ApplicationDbContext db, PublicAdvisory entity) => new()
    {
        Title = entity.Title, Slug = entity.Slug, Category = entity.Category, Priority = entity.Priority,
        Summary = entity.Summary, ContentHtml = entity.ContentHtml, ImageUrl = entity.ImageUrl,
        IssuingUnit = entity.IssuingUnit, PublishedAt = entity.PublishedAt, EffectiveUntil = entity.EffectiveUntil, IsPinned = entity.IsPinned
    };
    public override async Task<WriteResult> ApplyAsync(ApplicationDbContext db, PublicAdvisoryInput input,
        PublicAdvisory entity, OwnershipContext? context, bool create, CancellationToken token, ImportLookups? lookups = null)
    {
        var slug = !create ? entity.Slug : lookups is null
            ? await AdvisorySlugs.CreateAsync(db, input.Title, token)
            : input.Slug.Trim().ToLowerInvariant();
        if (create && lookups is not null && string.IsNullOrEmpty(slug))
            return WriteResult.Invalid("Advisory imports require an explicit slug.");
        if (create && lookups is not null && lookups.Exists("Slug:" + slug))
            return WriteResult.Invalid("An advisory already uses this slug, including a case variant.");
        var content = AdvisoryHtml.Clean(input.ContentHtml);
        if (content.Length > 100000) return WriteResult.Invalid("Cleaned content must be at most 100,000 characters.");
        entity.Title = input.Title.Trim(); entity.Slug = slug; entity.Category = input.Category; entity.Priority = input.Priority;
        entity.Summary = input.Summary; entity.ContentHtml = content; entity.ImageUrl = string.IsNullOrWhiteSpace(input.ImageUrl) ? null : input.ImageUrl;
        entity.IssuingUnit = input.IssuingUnit; entity.PublishedAt = WallTime(input.PublishedAt!.Value, create ? null : entity.PublishedAt);
        entity.EffectiveUntil = input.EffectiveUntil.HasValue ? WallTime(input.EffectiveUntil.Value, create ? null : entity.EffectiveUntil) : null; entity.IsPinned = input.IsPinned;
        return WriteResult.Success;
    }
}
