using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class AdvisoriesController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdvisoriesController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Advisories
    public async Task<IActionResult> Index(AdvisoryCategory? category, string? search)
    {
        var query = _db.PublicAdvisories.AsQueryable();

        if (category.HasValue)
        {
            query = query.Where(a => a.Category == category.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(s) || a.Summary.ToLower().Contains(s));
        }

        var advisories = await query
            .OrderByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.PublishedAt)
            .ToListAsync();

        var pinnedAlert = await _db.PublicAdvisories
            .FirstOrDefaultAsync(a => a.IsPinned && a.Priority == AdvisoryPriority.Urgent);

        var model = new AdvisoriesIndexViewModel
        {
            SelectedCategory = category,
            SearchTerm = search,
            Advisories = advisories,
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

        var advisory = await _db.PublicAdvisories
            .FirstOrDefaultAsync(a => a.Slug == targetSlug);

        if (advisory is null)
        {
            return NotFound();
        }

        // Increment view count
        advisory.ViewCount++;
        await _db.SaveChangesAsync();

        var related = await _db.PublicAdvisories
            .Where(a => a.Id != advisory.Id)
            .OrderByDescending(a => a.PublishedAt)
            .Take(3)
            .ToListAsync();

        var model = new AdvisoryDetailViewModel
        {
            Advisory = advisory,
            RelatedAdvisories = related
        };

        return View(model);
    }
}

public class AdvisoriesIndexViewModel
{
    public AdvisoryCategory? SelectedCategory { get; set; }
    public string? SearchTerm { get; set; }
    public IReadOnlyList<PublicAdvisory> Advisories { get; set; } = Array.Empty<PublicAdvisory>();
    public PublicAdvisory? PinnedAlert { get; set; }
}

public class AdvisoryDetailViewModel
{
    public PublicAdvisory Advisory { get; set; } = null!;
    public IReadOnlyList<PublicAdvisory> RelatedAdvisories { get; set; } = Array.Empty<PublicAdvisory>();
}
