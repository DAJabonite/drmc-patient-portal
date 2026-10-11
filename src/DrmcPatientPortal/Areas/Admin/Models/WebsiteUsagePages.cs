using System.Globalization;
using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Areas.Admin.Models;

public enum WebsiteUsagePeriod { Month, Day, Range }
public enum WebsiteUsageCoverage { NotCollected, Partial, Complete }

public sealed class WebsiteUsageFilter
{
    public WebsiteUsagePeriod Mode { get; set; } = WebsiteUsagePeriod.Month;
    public DateOnly? Day { get; set; }
    public string? Month { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public bool TryResolve(DateOnly today, out WebsiteUsageRange range, out string? error)
    {
        range = new(today, today);
        error = null;
        switch (Mode)
        {
            case WebsiteUsagePeriod.Day:
                Day ??= today;
                range = new(Day.Value, Day.Value);
                break;
            case WebsiteUsagePeriod.Month:
                Month ??= today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                if (Month.Length != 7 || !DateOnly.TryParseExact(Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var first)) { error = "Choose a valid month."; return false; }
                if (first > today) { error = "Choose a month that has started."; return false; }
                var last = first.AddDays(DateTime.DaysInMonth(first.Year, first.Month) - 1);
                range = new(first, last > today ? today : last);
                break;
            case WebsiteUsagePeriod.Range:
                if (!StartDate.HasValue || !EndDate.HasValue) { error = "Enter both start and end dates."; return false; }
                range = new(StartDate.Value, EndDate.Value);
                break;
            default:
                error = "Choose Day, Month, or Custom range."; return false;
        }
        if (range.Start > range.End) error = "The end date must be on or after the start date.";
        else if (range.End > today) error = "Choose dates on or before today in Asia/Manila.";
        else if (range.End.DayNumber - range.Start.DayNumber >= 366) error = "Choose a range of no more than 366 days.";
        return error is null;
    }
}

public sealed record WebsiteUsageRange(DateOnly Start, DateOnly End);
public sealed record WebsiteUsageRow(DateOnly Day, long? EstimatedVisitors, long? ActivePatients,
    WebsiteUsageCoverage Coverage, bool IsToday)
{
    public string CoverageLabel => Coverage switch
    {
        WebsiteUsageCoverage.NotCollected => "Not collected",
        WebsiteUsageCoverage.Complete => "Complete",
        _ => IsToday ? "Partial — today is incomplete" : "Partial",
    };
}

public sealed record WebsiteUsageObservation(DateOnly Day, long EstimatedVisitors, long ActivePatients,
    DateTimeOffset CoverageStartedUtc, DateTimeOffset LastHeartbeatUtc, bool HasGap)
{
    public WebsiteUsageRow ToRow(DateOnly today) => new(Day, EstimatedVisitors, ActivePatients,
        Day < today && !HasGap && CoverageStartedUtc <= WebsiteUsageClock.Start(Day) + WebsiteUsageClock.GapTolerance &&
        LastHeartbeatUtc >= WebsiteUsageClock.Start(Day.AddDays(1)) - WebsiteUsageClock.GapTolerance
            ? WebsiteUsageCoverage.Complete : WebsiteUsageCoverage.Partial, Day == today);
}

public sealed record WebsiteUsageReport(WebsiteUsageRange Range, DateTimeOffset GeneratedUtc, bool CollectionEnabled,
    IReadOnlyList<WebsiteUsageRow> Rows)
{
    public long VisitorDays => Rows.Sum(r => r.EstimatedVisitors ?? 0);
    public long PatientDays => Rows.Sum(r => r.ActivePatients ?? 0);
    public int CompleteDays => Rows.Count(r => r.Coverage == WebsiteUsageCoverage.Complete);
    public double? AverageVisitors => CompleteDays == 0 ? null : Rows.Where(r => r.Coverage == WebsiteUsageCoverage.Complete).Average(r => (double)r.EstimatedVisitors!.Value);
    public double? AveragePatients => CompleteDays == 0 ? null : Rows.Where(r => r.Coverage == WebsiteUsageCoverage.Complete).Average(r => (double)r.ActivePatients!.Value);
    public bool HasMeasurements => Rows.Any(r => r.Coverage != WebsiteUsageCoverage.NotCollected);
    public long ChartMaximum => Math.Max(1, Rows.Max(r => Math.Max(r.EstimatedVisitors ?? 0, r.ActivePatients ?? 0)));

    // SVG coordinates always use invariant formatting, regardless of admin request culture.
    // Separate segments preserve gaps instead of connecting across uncollected dates.
    public IReadOnlyList<string> ChartSegments(bool patients)
    {
        var maximum = ChartMaximum;
        var segments = new List<string>();
        var points = new List<string>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var value = patients ? Rows[i].ActivePatients : Rows[i].EstimatedVisitors;
            if (!value.HasValue)
            {
                if (points.Count > 0) { segments.Add(string.Join(" ", points)); points.Clear(); }
                continue;
            }
            var x = Rows.Count == 1 ? 400 : 40 + 720d * i / (Rows.Count - 1);
            var y = 210 - 180d * value.Value / maximum;
            points.Add(FormattableString.Invariant($"{x:0.##},{y:0.##}"));
        }
        if (points.Count > 0) segments.Add(string.Join(" ", points));
        return segments;
    }
}

public sealed record WebsiteUsagePage(WebsiteUsageFilter Filter, WebsiteUsageReport? Report);
