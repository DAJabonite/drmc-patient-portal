using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

// One owned metadata projection is shared by the lists, encounter summary and dashboard.
public sealed class PatientReportQueries(ApplicationDbContext db, IOptionsMonitor<PatientResultsOptions> options,
    RedStarReportAccess privateReports)
{
    public async Task<IReadOnlyList<PatientReportCard>> LaboratoriesAsync(string userId, CancellationToken token = default)
    {
        var rows = await SqlReadRetry.RunAsync(db, () => db.LabResults.AsNoTracking()
            .Where(l => l.Patient.PortalUserId == userId && !l.RequiresEncounterAssignment
                && (l.ClinicalEncounterId == null || l.Encounter!.PatientRecordId == l.PatientRecordId))
            .Select(l => new
            {
                l.Id, l.TestName, l.AccessionNumber, l.CollectedAt, l.ClinicalEncounterId, l.Status, l.ReleasedAt,
                l.ReportFileName, l.ReportUploadedAtUtc, l.PatientActivityAtUtc, l.Category,
                Reference = l.Encounter == null ? null : l.Encounter.EncounterReference,
                EncounterDate = l.Encounter == null ? (DateTime?)null : l.Encounter.EncounterDate
            }).ToListAsync(token), token);
        var now = PatientResultsDisclosure.ManilaNow;
        List<PatientReportCard> result = rows.Select(l => new PatientReportCard
        {
            Id = l.Id, Description = l.TestName, RequestReference = l.AccessionNumber, ExamDate = l.CollectedAt, ClinicalEncounterId = l.ClinicalEncounterId,
            EncounterReference = l.Reference, EncounterDate = l.EncounterDate, Status = LabStatus(l.Status, l.CollectedAt),
            Category = l.Category, HasPdf = l.ReportFileName != null,
            IsReleased = l.CollectedAt.HasValue && l.ReportFileName != null && l.Status == "Available" && l.ReleasedAt <= now && l.ReleasedAt >= l.CollectedAt,
            CanOpenPdf = options.CurrentValue.ShowFullResults && l.CollectedAt.HasValue
                && l.ReportFileName != null && l.Status == "Available" && l.ReleasedAt <= now && l.ReleasedAt >= l.CollectedAt,
            ActivityAt = Activity(l.PatientActivityAtUtc, l.ReportUploadedAtUtc, l.Status == "Available" && l.ReportFileName != null && l.ReleasedAt >= l.CollectedAt ? l.ReleasedAt : null, l.EncounterDate ?? l.CollectedAt, now)
        }).ToList();
        foreach (var report in await privateReports.GetOwnedReportsAsync(userId, token))
        {
            var released = RedStarReportAccess.CanOpenPdf(report, true, now);
            result.Add(new PatientReportCard
            {
                Id = report.Id, Source = "redstar", Description = report.Description, ExamDate = report.CollectedAt,
                ClinicalEncounterId = report.ClinicalEncounterId, EncounterReference = report.Encounter.EncounterReference,
                EncounterDate = report.Encounter.EncounterDate, Status = LabStatus(report.Status, report.CollectedAt),
                Category = LabCategory.SpecialDiagnostics, HasPdf = report.ReportFileName != null, IsReleased = released,
                CanOpenPdf = RedStarReportAccess.CanOpenPdf(report, options.CurrentValue.ShowFullResults, now),
                ActivityAt = Activity(report.PatientActivityAtUtc, report.ReportUploadedAtUtc, released ? report.ReleasedAt : null,
                    report.Encounter.EncounterDate, now)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<PatientReportCard>> RadiologyAsync(string userId, CancellationToken token = default)
    {
        var rows = await SqlReadRetry.RunAsync(db, () => db.RadiologyStudies.AsNoTracking()
            .Where(r => r.Patient.PortalUserId == userId && !r.RequiresEncounterAssignment
                && (r.ClinicalEncounterId == null || r.Encounter!.PatientRecordId == r.PatientRecordId))
            .Select(r => new
            {
                r.Id, r.StudyName, r.AccessionNumber, r.Modality, r.PerformedAt, r.ClinicalEncounterId, r.Status, r.ReleasedAt,
                r.ReportFileName, r.ReportUploadedAtUtc, r.PatientActivityAtUtc,
                Reference = r.Encounter == null ? null : r.Encounter.EncounterReference,
                EncounterDate = r.Encounter == null ? (DateTime?)null : r.Encounter.EncounterDate
            }).ToListAsync(token), token);
        var now = PatientResultsDisclosure.ManilaNow;
        return rows.Select(r => new PatientReportCard
        {
            Id = r.Id, Source = "radiology", RequestReference = r.AccessionNumber, Description = string.IsNullOrWhiteSpace(r.StudyName) ? r.Modality.PdfDisplayName() : r.StudyName,
            ExamDate = r.PerformedAt, ClinicalEncounterId = r.ClinicalEncounterId, EncounterReference = r.Reference,
            EncounterDate = r.EncounterDate, Modality = r.Modality,
            Status = r.Status == RadiologyStatus.InProgress && r.PerformedAt == null ? "Requested"
                : r.Status == RadiologyStatus.InProgress ? "Awaiting report" : r.Status.DisplayName(),
            HasPdf = r.ReportFileName != null,
            IsReleased = r.PerformedAt.HasValue && r.ReportFileName != null && PatientResultsDisclosure.IsReleased(r.Status, r.ReleasedAt, now) && r.ReleasedAt >= r.PerformedAt,
            CanOpenPdf = options.CurrentValue.ShowFullResults && r.PerformedAt.HasValue
                && r.ReportFileName != null && PatientResultsDisclosure.IsReleased(r.Status, r.ReleasedAt, now) && r.ReleasedAt >= r.PerformedAt,
            ActivityAt = Activity(r.PatientActivityAtUtc, r.ReportUploadedAtUtc, r.ReportFileName != null && PatientResultsDisclosure.IsReleased(r.Status, r.ReleasedAt, now) && r.ReleasedAt >= r.PerformedAt ? r.ReleasedAt : null, r.EncounterDate ?? r.PerformedAt, now)
        }).ToList();
    }

    public static string LabStatus(string status, DateTime? actualDate) => status == "In progress"
        ? actualDate.HasValue ? "Awaiting report" : "Requested" : status;

    public static DateTime Activity(DateTime? changedUtc, DateTime? uploadedUtc, DateTime? released,
        DateTime? fallback, DateTime now)
    {
        var activity = fallback ?? DateTime.MinValue;
        if (changedUtc.HasValue) activity = Max(activity, ClinicalClock.WallTime(new DateTimeOffset(DateTime.SpecifyKind(changedUtc.Value, DateTimeKind.Utc))));
        if (uploadedUtc.HasValue) activity = Max(activity, ClinicalClock.WallTime(new DateTimeOffset(DateTime.SpecifyKind(uploadedUtc.Value, DateTimeKind.Utc))));
        if (released.HasValue && released <= now) activity = Max(activity, released.Value);
        return activity;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static string? NormalizeStatus(string? status) => status is "pending" or "released" ? status : null;
    public static string? NormalizeSearch(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(search.Trim().Length, 200)];

    public static (DateTime? Start, DateTime? End, string? Error) DateBounds(string? range, DateTime? start, DateTime? end)
    {
        var today = ClinicalClock.Today;
        return range switch
        {
            "30d" => (today.AddDays(-30), null, null),
            "6m" => (today.AddMonths(-6), null, null),
            "year" => (new DateTime(today.Year, 1, 1), null, null),
            "custom" when !start.HasValue || !end.HasValue => (null, null, "Choose both a start date and an end date."),
            "custom" when start!.Value.Date > end!.Value.Date => (null, null, "The start date must be on or before the end date."),
            "custom" when start!.Value.Date > today || end!.Value.Date > today => (null, null, "Encounter dates cannot be in the future."),
            "custom" => (start!.Value.Date, end!.Value.Date.AddDays(1), null),
            _ => (null, null, null)
        };
    }

    public static PatientReportPage Page(IEnumerable<PatientReportCard> owned, string? search, string? status,
        DateTime? start, DateTime? end, bool encounterDateFilter, int page, int legacyPage)
    {
        var query = owned.Where(r => search == null || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || r.RequestReference.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (r.EncounterReference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        query = status switch
        {
            "pending" => query.Where(r => !r.IsReleased && r.Status != "Cancelled"),
            "released" => query.Where(r => r.IsReleased),
            _ => query
        };
        var matched = query.ToList();
        var legacy = encounterDateFilter ? [] : matched.Where(r => !r.ClinicalEncounterId.HasValue)
            .OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).ToList();
        var linked = matched.Where(r => r.ClinicalEncounterId.HasValue && r.EncounterDate.HasValue
            && (!start.HasValue || r.EncounterDate >= start) && (!end.HasValue || r.EncounterDate < end)).ToList();
        var groups = linked.GroupBy(r => r.ClinicalEncounterId!.Value)
            .OrderByDescending(g => g.Max(r => r.ActivityAt)).ThenByDescending(g => g.First().EncounterDate)
            .ThenByDescending(g => g.Key).ToList();
        var pagination = Pagination(groups.Count, page);
        var legacyPagination = Pagination(legacy.Count, legacyPage, "legacyPage");
        return new()
        {
            EncounterGroups = groups.Skip(pagination.Offset).Take(PortalPagination.Size).Select(g =>
                new PatientEncounterReportGroup(g.Key, g.First().EncounterReference ?? "", g.First().EncounterDate!.Value,
                    g.OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).ToList())).ToList(),
            LegacyReports = legacy.Skip(legacyPagination.Offset).Take(PortalPagination.Size).ToList(),
            Pagination = pagination, LegacyPagination = legacyPagination,
            RequestCount = linked.Count + legacy.Count, PdfCount = linked.Count(r => r.HasPdf) + legacy.Count(r => r.HasPdf),
            LegacyExcluded = encounterDateFilter && matched.Any(r => !r.ClinicalEncounterId.HasValue)
        };
    }

    public static PortalPagination Pagination(int total, int page, string parameter = "page") =>
        new(total, Math.Clamp(page, 1, Math.Max(1, (total + PortalPagination.Size - 1) / PortalPagination.Size)), parameter);
}
