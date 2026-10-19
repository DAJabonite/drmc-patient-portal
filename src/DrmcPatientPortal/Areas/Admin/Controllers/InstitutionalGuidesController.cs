using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class InstitutionalGuidesController(ApplicationDbContext db, AdminWrites writes,
    InstitutionalGuideService guides, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var revisions = await db.InstitutionalGuideRevisions.AsNoTracking().ToListAsync(cancellationToken);
        var today = guides.Today;
        return View(new InstitutionalGuideList(InstitutionalGuideCatalog.All.Select(pair =>
        {
            var history = revisions.Where(r => r.GuideKey == pair.Key).ToArray();
            return new InstitutionalGuideListRow(pair.Key, pair.Value,
                history.Where(r => r.EffectiveOn <= today).OrderByDescending(r => r.EffectiveOn)
                    .ThenByDescending(r => r.RevisionNumber).FirstOrDefault(), Scheduled(history, today));
        }).ToArray(), today));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, int? sourceRevision, CancellationToken cancellationToken)
    {
        if (!InstitutionalGuideCatalog.All.ContainsKey(id)) return NotFound();
        var head = await db.InstitutionalGuideHeads.AsNoTracking().SingleOrDefaultAsync(h => h.Key == id, cancellationToken);
        if (head is null) return NotFound();
        var active = await guides.ActiveRevisionAsync(id, cancellationToken);
        var source = sourceRevision.HasValue
            ? await db.InstitutionalGuideRevisions.AsNoTracking().SingleOrDefaultAsync(r =>
                r.GuideKey == id && r.RevisionNumber == sourceRevision.Value, cancellationToken)
            : active;
        if (sourceRevision.HasValue && source is null) return NotFound();
        var input = new InstitutionalGuideInput
        {
            RowVersion = Convert.ToBase64String(head.RowVersion), SourceRevisionNumber = source?.RevisionNumber,
            EffectiveOn = source is not null && source.EffectiveOn > guides.Today ? source.EffectiveOn : guides.Today
        };
        foreach (var culture in InstitutionalGuideCatalog.Cultures)
        {
            var locale = input.Locale(culture);
            var json = source?.ContentFor(culture);
            locale.Enabled = json is not null;
            locale.Content = json is null ? InstitutionalGuideDefaults.Get(id, culture)
                : GuideContentCodec.Deserialize(id, json, source!.SchemaVersion);
        }
        return await EditPage(id, input, active?.RevisionNumber, cancellationToken);
    }

    [HttpPost, RequestFormLimits(ValueCountLimit = 20000), RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Edit(string id, [Bind(Prefix = "Input")] InstitutionalGuideInput input,
        CancellationToken cancellationToken)
    {
        if (!InstitutionalGuideCatalog.All.ContainsKey(id)) return NotFound();
        var version = AdminAudit.Version(input.RowVersion);
        // A missing or malformed concurrency token is always a conflict, even on a first save.
        if (version is null) return AdminConflicts.Missing(this);
        var currentHead = await db.InstitutionalGuideHeads.AsNoTracking().SingleOrDefaultAsync(h => h.Key == id, cancellationToken);
        if (currentHead is null || !version.SequenceEqual(currentHead.RowVersion)) return AdminConflicts.Missing(this);
        if (!input.EffectiveOn.HasValue || input.EffectiveOn.Value < guides.Today)
            ModelState.AddModelError("Input.EffectiveOn", "Choose today or a future date in the Philippines.");
        var snapshots = new Dictionary<string, string?>();
        foreach (var culture in InstitutionalGuideCatalog.Cultures)
        {
            var locale = input.Locale(culture);
            snapshots[culture] = null;
            if (!locale.Enabled)
            {
                var prefix = "Input." + LocaleProperty(culture) + ".Content";
                foreach (var field in ModelState.Keys.Where(k => k.Equals(prefix, StringComparison.Ordinal) ||
                    k.StartsWith(prefix + ".", StringComparison.Ordinal)).ToArray()) ModelState.Remove(field);
                continue;
            }
            try { snapshots[culture] = GuideContentCodec.SerializeAndClean(id, locale.Content); }
            catch (ValidationException error) { ModelState.AddModelError("Input." + LocaleProperty(culture), error.Message); }
        }
        if (ModelState.IsValid)
        {
            var result = await writes.RunAsync(async context =>
            {
                var head = await context.InstitutionalGuideHeads.SingleOrDefaultAsync(h => h.Key == id, cancellationToken);
                if (head is null || !version.SequenceEqual(head.RowVersion)) return WriteResult.Stale;
                InstitutionalGuideRevision? source = null;
                if (input.SourceRevisionNumber.HasValue)
                {
                    source = await context.InstitutionalGuideRevisions.AsNoTracking().SingleOrDefaultAsync(r =>
                        r.GuideKey == id && r.RevisionNumber == input.SourceRevisionNumber.Value, cancellationToken);
                    if (source is null) return WriteResult.Invalid("The source revision does not belong to this guide. Reload the editor.");
                }
                // Recheck the calendar boundary inside the write transaction.
                if (input.EffectiveOn!.Value < guides.Today)
                    return WriteResult.Invalid("The effective date has passed. Choose today or a future date.");
                var now = clock.GetUtcNow().UtcDateTime;
                var revision = new InstitutionalGuideRevision
                {
                    GuideKey = id, RevisionNumber = checked(head.LatestRevisionNumber + 1),
                    SchemaVersion = InstitutionalGuideCatalog.SchemaVersion, SourceRevisionNumber = source?.RevisionNumber,
                    CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("An audit actor is required."),
                    CreatedAtUtc = now, LastReviewedAtUtc = now, EffectiveOn = input.EffectiveOn.Value,
                    EnglishJson = snapshots["en"], FilipinoJson = snapshots["fil"], CebuanoJson = snapshots["ceb"]
                };
                context.Entry(head).Property(h => h.RowVersion).OriginalValue = version;
                head.LatestRevisionNumber = revision.RevisionNumber;
                context.InstitutionalGuideRevisions.Add(revision);
                await context.SaveChangesAsync(cancellationToken);
                await AdminAudit.AddAsync(context, User, "Edit", "InstitutionalGuides", id,
                    before: Snapshot(id, source), after: Snapshot(id, revision), cancellationToken: cancellationToken);
                return WriteResult.Success;
            }, cancellationToken);
            if (result.Succeeded)
            {
                TempData["AdminSuccess"] = "Guide revision saved.";
                return RedirectToAction(nameof(History), new { area = "Admin", id });
            }
            if (result.Conflict) Response.StatusCode = StatusCodes.Status409Conflict;
            ModelState.AddModelError("", result.Error!);
        }
        var active = await guides.ActiveRevisionAsync(id, cancellationToken);
        return await EditPage(id, input, active?.RevisionNumber, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> History(string id, CancellationToken cancellationToken)
    {
        if (!InstitutionalGuideCatalog.All.TryGetValue(id, out var title)) return NotFound();
        var revisions = await db.InstitutionalGuideRevisions.AsNoTracking().Where(r => r.GuideKey == id)
            .OrderByDescending(r => r.RevisionNumber).ToListAsync(cancellationToken);
        var active = await guides.ActiveRevisionAsync(id, cancellationToken);
        return View(new InstitutionalGuideHistory(id, title, active?.RevisionNumber, guides.Today, revisions));
    }

    [HttpGet]
    public async Task<IActionResult> Revision(string id, int revision, CancellationToken cancellationToken)
    {
        if (!InstitutionalGuideCatalog.All.TryGetValue(id, out var title)) return NotFound();
        var record = await db.InstitutionalGuideRevisions.AsNoTracking().SingleOrDefaultAsync(r =>
            r.GuideKey == id && r.RevisionNumber == revision, cancellationToken);
        if (record is null) return NotFound();
        return View(new InstitutionalGuideRevisionPage(id, title, record, InstitutionalGuideCatalog.Cultures.ToDictionary(
            culture => culture, culture => record.ContentFor(culture) is { } json
                ? GuideContentCodec.Deserialize(id, json, record.SchemaVersion) : InstitutionalGuideDefaults.Get(id, culture))));
    }

    private async Task<IActionResult> EditPage(string key, InstitutionalGuideInput input, int? active, CancellationToken token)
    {
        // Disabled submitted graphs are ignored completely, including on a validation-error render.
        var source = input.SourceRevisionNumber.HasValue
            ? await db.InstitutionalGuideRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.GuideKey == key &&
                r.RevisionNumber == input.SourceRevisionNumber.Value, token) : null;
        foreach (var culture in InstitutionalGuideCatalog.Cultures)
        {
            var locale = input.Locale(culture);
            if (locale.Enabled) continue;
            locale.Content = source?.ContentFor(culture) is { } json
                ? GuideContentCodec.Deserialize(key, json, source.SchemaVersion) : InstitutionalGuideDefaults.Get(key, culture);
        }
        var scheduled = await db.InstitutionalGuideRevisions.AsNoTracking().Where(r => r.GuideKey == key && r.EffectiveOn > guides.Today)
            .ToListAsync(token);
        return View("Edit", new InstitutionalGuideEditPage(key, InstitutionalGuideCatalog.All[key], input, active,
            guides.Today, Scheduled(scheduled, guides.Today)));
    }

    private static IReadOnlyList<InstitutionalGuideRevision> Scheduled(IEnumerable<InstitutionalGuideRevision> history, DateOnly today) =>
        history.Where(r => r.EffectiveOn > today).GroupBy(r => r.EffectiveOn)
            .Select(group => group.OrderByDescending(r => r.RevisionNumber).First()).OrderBy(r => r.EffectiveOn).ToArray();
    private static string LocaleProperty(string culture) => culture switch { "fil" => "Filipino", "ceb" => "Cebuano", _ => "English" };
    private static Dictionary<string, object?> Snapshot(string key, InstitutionalGuideRevision? revision) => new()
    {
        ["GuideKey"] = key, ["RevisionNumber"] = revision?.RevisionNumber,
        ["SchemaVersion"] = revision?.SchemaVersion ?? InstitutionalGuideCatalog.SchemaVersion,
        ["SourceRevisionNumber"] = revision?.SourceRevisionNumber, ["EffectiveOn"] = revision?.EffectiveOn,
        ["CreatedByUserId"] = revision?.CreatedByUserId, ["CreatedAtUtc"] = revision?.CreatedAtUtc,
        ["LastReviewedAtUtc"] = revision?.LastReviewedAtUtc,
        ["EnglishJson"] = revision?.EnglishJson, ["FilipinoJson"] = revision?.FilipinoJson, ["CebuanoJson"] = revision?.CebuanoJson,
        ["BundledDefaults"] = revision is null ? InstitutionalGuideCatalog.Cultures.ToDictionary(c => c,
            c => GuideContentCodec.Serialize(InstitutionalGuideDefaults.Get(key, c))) : null
    };
}
