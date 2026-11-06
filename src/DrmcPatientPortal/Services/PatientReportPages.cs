using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed partial class PatientReportQueries
{
    private sealed class ReportRow
    {
        public int Id { get; init; }
        public int Source { get; init; }
        public int? EncounterId { get; init; }
        public string? EncounterReference { get; init; }
        public DateTime? EncounterDate { get; init; }
        public DateTime? ExamDate { get; init; }
        public DateTime? ChangedUtc { get; init; }
        public DateTime? UploadedUtc { get; init; }
        public DateTime? ReleasedActivity { get; init; }
        public bool Released { get; init; }
        public bool Cancelled { get; init; }
        public bool HasPdf { get; init; }
        public LabCategory? Category { get; init; }
        public RadiologyModality? Modality { get; init; }
        public string Description { get; init; } = "";
        public string Reference { get; init; } = "";
    }

    private sealed class ReportKey
    {
        public int Id { get; init; }
        public int Source { get; init; }
        public int? EncounterId { get; init; }
        public string? EncounterReference { get; init; }
        public DateTime? EncounterDate { get; init; }
        public DateTime ActivityAt { get; init; }
        public bool HasPdf { get; init; }
        public bool Released { get; init; }
        public bool Cancelled { get; init; }
        public LabCategory? Category { get; init; }
        public RadiologyModality? Modality { get; init; }
        public string Description { get; init; } = "";
        public string Reference { get; init; } = "";
    }

    private IQueryable<ReportRow> LaboratoryRows(string userId)
    {
        var now = PatientResultsDisclosure.ManilaNow;
        return db.LabResults.AsNoTracking().Where(l => l.Patient.PortalUserId == userId && !l.RequiresEncounterAssignment
            && (l.ClinicalEncounterId == null || l.Encounter!.PatientRecordId == l.PatientRecordId))
            .Select(l => new ReportRow
            {
                Id = l.Id, Source = 0, EncounterId = l.ClinicalEncounterId,
                EncounterReference = l.Encounter == null ? null : l.Encounter.EncounterReference,
                EncounterDate = l.Encounter == null ? null : l.Encounter.EncounterDate, ExamDate = l.CollectedAt,
                ChangedUtc = l.PatientActivityAtUtc, UploadedUtc = l.ReportUploadedAtUtc,
                ReleasedActivity = l.Status == "Available" && l.ReportFileName != null && l.ReleasedAt >= l.CollectedAt ? l.ReleasedAt : null,
                Released = l.CollectedAt.HasValue && l.ReportFileName != null && l.Status == "Available" && l.ReleasedAt <= now && l.ReleasedAt >= l.CollectedAt,
                HasPdf = l.ReportFileName != null, Cancelled = l.Status == "Cancelled", Category = l.Category,
                Modality = null, Description = l.TestName, Reference = l.AccessionNumber
            });
    }

    private static IQueryable<ReportRow> PrivateRows(IQueryable<RedStarLabReport> reports)
    {
        var now = PatientResultsDisclosure.ManilaNow;
        // Description is encrypted. It is read only when displaying a selected report or matching a private search.
        return reports.Select(r => new ReportRow
        {
            Id = r.Id, Source = 1, EncounterId = r.ClinicalEncounterId,
            EncounterReference = r.Encounter.EncounterReference, EncounterDate = r.Encounter.EncounterDate,
            ExamDate = r.CollectedAt, ChangedUtc = r.PatientActivityAtUtc, UploadedUtc = r.ReportUploadedAtUtc,
            ReleasedActivity = r.CollectedAt.HasValue && r.ReportFileName != null && r.Status == "Available" && r.ReleasedAt <= now && r.ReleasedAt >= r.CollectedAt ? r.ReleasedAt : null,
            Released = r.CollectedAt.HasValue && r.ReportFileName != null && r.Status == "Available" && r.ReleasedAt <= now && r.ReleasedAt >= r.CollectedAt,
            HasPdf = r.ReportFileName != null, Cancelled = r.Status == "Cancelled", Category = LabCategory.SpecialDiagnostics,
            Modality = null, Description = "", Reference = ""
        });
    }

    private IQueryable<ReportRow> RadiologyRows(string userId)
    {
        var now = PatientResultsDisclosure.ManilaNow;
        return db.RadiologyStudies.AsNoTracking().Where(r => r.Patient.PortalUserId == userId && !r.RequiresEncounterAssignment
            && (r.ClinicalEncounterId == null || r.Encounter!.PatientRecordId == r.PatientRecordId))
            .Select(r => new ReportRow
            {
                Id = r.Id, Source = 2, EncounterId = r.ClinicalEncounterId,
                EncounterReference = r.Encounter == null ? null : r.Encounter.EncounterReference,
                EncounterDate = r.Encounter == null ? null : r.Encounter.EncounterDate, ExamDate = r.PerformedAt,
                ChangedUtc = r.PatientActivityAtUtc, UploadedUtc = r.ReportUploadedAtUtc,
                ReleasedActivity = r.ReportFileName != null && (r.Status == RadiologyStatus.Final || r.Status == RadiologyStatus.Amended)
                    && r.ReleasedAt <= now && r.ReleasedAt >= r.PerformedAt ? r.ReleasedAt : null,
                Released = r.PerformedAt.HasValue && r.ReportFileName != null && (r.Status == RadiologyStatus.Final || r.Status == RadiologyStatus.Amended)
                    && r.ReleasedAt <= now && r.ReleasedAt >= r.PerformedAt,
                HasPdf = r.ReportFileName != null, Cancelled = false, Category = null, Modality = r.Modality,
                Description = r.StudyName,
                Reference = r.AccessionNumber
            });
    }

    private static IQueryable<ReportKey> Keys(IQueryable<ReportRow> rows)
    {
        var now = PatientResultsDisclosure.ManilaNow;
        return from row in rows
            let fallback = row.EncounterDate ?? row.ExamDate ?? DateTime.MinValue
            let changed = row.ChangedUtc.HasValue ? row.ChangedUtc.Value.AddHours(8) : DateTime.MinValue
            let uploaded = row.UploadedUtc.HasValue ? row.UploadedUtc.Value.AddHours(8) : DateTime.MinValue
            let update = changed > uploaded ? changed : uploaded
            let released = row.ReleasedActivity.HasValue && row.ReleasedActivity <= now ? row.ReleasedActivity!.Value : DateTime.MinValue
            let report = released > fallback ? released : fallback
            select new ReportKey
            {
                Id = row.Id, Source = row.Source, EncounterId = row.EncounterId, EncounterReference = row.EncounterReference,
                EncounterDate = row.EncounterDate, ActivityAt = update > report ? update : report, HasPdf = row.HasPdf,
                Released = row.Released, Cancelled = row.Cancelled, Category = row.Category, Modality = row.Modality,
                Description = row.Description, Reference = row.Reference
            };
    }

    public async Task<(PatientReportPage Page, int Total)> LaboratoryPageAsync(string userId, LabCategory? category,
        string? search, string? status, DateTime? start, DateTime? end, bool encounterDateFilter,
        int page, int legacyPage, CancellationToken token = default)
    {
        var privateQuery = await privateReports.OwnedReportsAsync(userId, token);
        var rows = LaboratoryRows(userId).Concat(PrivateRows(privateQuery));
        var total = await SqlReadRetry.RunAsync(db, () => rows.CountAsync(token), token);
        if (category.HasValue) rows = rows.Where(r => r.Category == category);
        if (search is not null)
        {
            // SQL linguistic collations do not preserve the existing ordinal search (for example, ss versus ß).
            var laboratoryIds = await MatchingIdsAsync(rows.Where(r => r.Source == 0), search, false, token);
            // Keep ciphertext out of SQL text predicates.
            var privateMatches = new List<int>();
            await foreach (var report in privateQuery.Select(r => new { r.Id, r.Description, r.Encounter.EncounterReference }).AsAsyncEnumerable().WithCancellation(token))
                if (report.Description.Contains(search, StringComparison.OrdinalIgnoreCase) || report.EncounterReference.Contains(search, StringComparison.OrdinalIgnoreCase)) privateMatches.Add(report.Id);
            var privateIds = privateMatches.ToArray();
            rows = rows.Where(r => r.Source == 1 ? EF.Parameter(privateIds).Contains(r.Id)
                : EF.Parameter(laboratoryIds).Contains(r.Id));
        }
        return (await PageAsync(userId, Keys(rows), false, status, start, end, encounterDateFilter, page, legacyPage, token), total);
    }

    public async Task<(PatientReportPage Page, int Total)> RadiologyPageAsync(string userId, RadiologyModality? modality,
        string? search, string? status, DateTime? start, DateTime? end, bool encounterDateFilter,
        int page, int legacyPage, CancellationToken token = default)
    {
        var rows = RadiologyRows(userId);
        var total = await SqlReadRetry.RunAsync(db, () => rows.CountAsync(token), token);
        if (modality.HasValue) rows = rows.Where(r => r.Modality == modality);
        if (search is not null)
        {
            var ids = await MatchingIdsAsync(rows, search, true, token);
            rows = rows.Where(r => EF.Parameter(ids).Contains(r.Id));
        }
        return (await PageAsync(userId, Keys(rows), true, status, start, end, encounterDateFilter, page, legacyPage, token), total);
    }

    private static async Task<int[]> MatchingIdsAsync(IQueryable<ReportRow> rows, string search, bool radiology, CancellationToken token)
    {
        var matches = new List<int>();
        await foreach (var row in rows.Select(r => new { r.Id, r.Description, r.Reference, r.EncounterReference, r.Modality })
            .AsAsyncEnumerable().WithCancellation(token))
        {
            var description = radiology && string.IsNullOrWhiteSpace(row.Description)
                ? row.Modality!.Value.PdfDisplayName() : row.Description;
            if (description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Reference.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (row.EncounterReference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                matches.Add(row.Id);
        }
        return matches.ToArray();
    }

    private async Task<PatientReportPage> PageAsync(string userId, IQueryable<ReportKey> keys, bool radiology,
        string? status, DateTime? start, DateTime? end, bool encounterDateFilter, int page, int legacyPage, CancellationToken token)
    {
        keys = status switch { "pending" => keys.Where(r => !r.Released && !r.Cancelled), "released" => keys.Where(r => r.Released), _ => keys };
        var legacy = keys.Where(r => !r.EncounterId.HasValue && !encounterDateFilter);
        var linked = keys.Where(r => r.EncounterId.HasValue && r.EncounterDate.HasValue
            && (!start.HasValue || r.EncounterDate >= start) && (!end.HasValue || r.EncounterDate < end));
        var groups = linked.GroupBy(r => r.EncounterId).Select(g => new
        { Id = g.Key, Activity = g.Max(r => r.ActivityAt), Date = g.Max(r => r.EncounterDate) });
        var pagination = Pagination(await SqlReadRetry.RunAsync(db, () => groups.CountAsync(token), token), page);
        var legacyPagination = Pagination(await SqlReadRetry.RunAsync(db, () => legacy.CountAsync(token), token), legacyPage, "legacyPage");
        var groupIds = await SqlReadRetry.RunAsync(db, () => groups.OrderByDescending(g => g.Activity).ThenByDescending(g => g.Date).ThenByDescending(g => g.Id)
            .Skip(pagination.Offset).Take(PortalPagination.Size).Select(g => g.Id).ToArrayAsync(token), token);
        var selected = await SqlReadRetry.RunAsync(db, () => linked.Where(r => EF.Parameter(groupIds).Contains(r.EncounterId)).ToListAsync(token), token);
        selected.AddRange(await SqlReadRetry.RunAsync(db, () => legacy.OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id)
            .Skip(legacyPagination.Offset).Take(PortalPagination.Size).ToListAsync(token), token));
        var cards = radiology
            ? await RadiologyAsync(userId, token, ids: selected.Select(r => r.Id).ToArray())
            : await LaboratoriesAsync(userId, token, laboratoryIds: selected.Where(r => r.Source == 0).Select(r => r.Id).ToArray(),
                privateIds: selected.Where(r => r.Source == 1).Select(r => r.Id).ToArray());
        return new PatientReportPage
        {
            EncounterGroups = groupIds.Select(id => cards.Where(r => r.ClinicalEncounterId == id).OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).ToList())
                .Where(reports => reports.Count > 0).Select(reports => new PatientEncounterReportGroup(reports[0].ClinicalEncounterId!.Value,
                    reports[0].EncounterReference ?? "", reports[0].EncounterDate!.Value, reports)).ToList(),
            LegacyReports = cards.Where(r => !r.ClinicalEncounterId.HasValue).OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).ToList(),
            Pagination = pagination, LegacyPagination = legacyPagination,
            RequestCount = await SqlReadRetry.RunAsync(db, () => linked.CountAsync(token), token) + legacyPagination.Total,
            PdfCount = await SqlReadRetry.RunAsync(db, () => linked.CountAsync(r => r.HasPdf, token), token)
                + await SqlReadRetry.RunAsync(db, () => legacy.CountAsync(r => r.HasPdf, token), token),
            LegacyExcluded = encounterDateFilter && await SqlReadRetry.RunAsync(db, () => keys.AnyAsync(r => !r.EncounterId.HasValue, token), token)
        };
    }

    public async Task<(IReadOnlyList<PatientReportCard> Reports, int Total)> LaboratoryPreviewAsync(string userId, CancellationToken token = default)
    {
        var keys = Keys(LaboratoryRows(userId).Concat(PrivateRows(await privateReports.OwnedReportsAsync(userId, token))));
        var total = await SqlReadRetry.RunAsync(db, () => keys.CountAsync(token), token);
        var selected = await SqlReadRetry.RunAsync(db, () => keys.OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).Take(2).ToListAsync(token), token);
        var reports = await LaboratoriesAsync(userId, token, laboratoryIds: selected.Where(r => r.Source == 0).Select(r => r.Id).ToArray(),
            privateIds: selected.Where(r => r.Source == 1).Select(r => r.Id).ToArray());
        return (reports.OrderByDescending(r => r.ActivityAt).ThenBy(r => r.Source).ThenByDescending(r => r.Id).ToList(), total);
    }
}
