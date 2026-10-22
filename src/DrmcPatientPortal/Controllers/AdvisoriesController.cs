using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class AdvisoriesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public AdvisoriesController(ApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    // GET /Advisories
    public async Task<IActionResult> Index(AdvisoryCategory? category, string? search, int page = 1)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var query = _db.PublicAdvisories.Where(a => a.PublishedAt <= now && (a.EffectiveUntil == null || a.EffectiveUntil > now));

        if (category.HasValue)
        {
            query = query.Where(a => a.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            s = s.Length > 200 ? s[..200] : s;
            query = query.Where(a => a.Title.ToLower().Contains(s) || a.Summary.ToLower().Contains(s));
        }

        var pagination = await PortalPagination.CreateAsync(query, _db, page);
        ViewData["Pagination"] = pagination;
        var advisories = await SqlReadRetry.RunAsync(_db, () => query.AsNoTracking()
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PublishedAt)
            .ThenByDescending(a => a.Id).Skip(pagination.Offset).Take(PortalPagination.Size)
            .ToListAsync());

        var pinnedAlert = await SqlReadRetry.RunAsync(_db, () => _db.PublicAdvisories
            .Where(a => a.PublishedAt <= now && (a.EffectiveUntil == null || a.EffectiveUntil > now))
            .OrderByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(a => a.IsPinned && a.Priority == AdvisoryPriority.Urgent));

        var model = new AdvisoriesIndexViewModel
        {
            SelectedCategory = category,
            SearchTerm = search,
            Advisories = advisories,
            TotalCount = pagination.Total,
            PinnedAlert = pinnedAlert
        };

        return View(model);
    }

    // GET /Advisories/Details/dengue-4s-prevention-alert or /Advisories/Details?slug=...
    [HttpGet]
    public async Task<IActionResult> Details(string? id, string? slug)
    {
        var targetSlug = slug ?? id;
        if (string.IsNullOrWhiteSpace(targetSlug))
        {
            return RedirectToAction(nameof(Index));
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var advisory = await SqlReadRetry.RunAsync(_db, () => _db.PublicAdvisories
            .FirstOrDefaultAsync(a => a.Slug == targetSlug && a.PublishedAt <= now));

        if (advisory is null)
        {
            return NotFound();
        }

        // Increment view count
        await _db.PublicAdvisories.Where(a => a.Id == advisory.Id && a.ViewCount < int.MaxValue)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.ViewCount, a => a.ViewCount + 1));
        if (advisory.ViewCount < int.MaxValue) advisory.ViewCount++;

        var related = await SqlReadRetry.RunAsync(_db, () => _db.PublicAdvisories
            .Where(a => a.Id != advisory.Id && a.PublishedAt <= now && (a.EffectiveUntil == null || a.EffectiveUntil > now))
            .OrderByDescending(a => a.PublishedAt)
            .Take(3)
            .ToListAsync());

        var model = new AdvisoryDetailViewModel
        {
            Advisory = advisory,
            IsExpired = advisory.EffectiveUntil <= now,
            RelatedAdvisories = related
        };

        return View(model);
    }
}

public class AdvisoriesIndexViewModel
{
    public int TotalCount { get; set; }
    public AdvisoryCategory? SelectedCategory { get; set; }
    public string? SearchTerm { get; set; }
    public IReadOnlyList<PublicAdvisory> Advisories { get; set; } = Array.Empty<PublicAdvisory>();
    public PublicAdvisory? PinnedAlert { get; set; }
}

public class AdvisoryDetailViewModel
{
    public bool IsExpired { get; set; }
    public PublicAdvisory Advisory { get; set; } = null!;
    public string SanitizedContentHtml => AdvisoryHtml.Clean(Advisory.ContentHtml);
    public IReadOnlyList<PublicAdvisory> RelatedAdvisories { get; set; } = Array.Empty<PublicAdvisory>();
}
