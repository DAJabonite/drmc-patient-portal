using System.Globalization;
using System.Text;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class WebsiteUsageReports(ApplicationDbContext db, TimeProvider clock, IOptions<WebsiteUsageOptions> options)
{
    public DateOnly Today => WebsiteUsageClock.Day(clock.GetUtcNow());

    public async Task<WebsiteUsageReport> ReadAsync(WebsiteUsageRange range, CancellationToken token = default)
    {
        var generated = clock.GetUtcNow();
        var today = WebsiteUsageClock.Day(generated);
        var observations = await SqlReadRetry.RunAsync(db, () => db.WebsiteUsageDays.AsNoTracking()
            .Where(d => d.Day >= range.Start && d.Day <= range.End)
            .Select(d => new WebsiteUsageObservation(d.Day, d.EstimatedVisitors, d.ActivePatients,
                d.CoverageStartedUtc, d.LastHeartbeatUtc, d.HasGap)).ToListAsync(token), token);
        var lookup = observations.ToDictionary(d => d.Day);
        var rows = new List<WebsiteUsageRow>();
        for (var index = range.Start.DayNumber; index <= range.End.DayNumber; index++)
        {
            var day = DateOnly.FromDayNumber(index);
            rows.Add(lookup.TryGetValue(day, out var observation) ? observation.ToRow(today) :
                new(day, null, null, WebsiteUsageCoverage.NotCollected, day == today));
        }
        return new(range, generated, options.Value.Enabled, rows);
    }

    public Task<WebsiteUsageReport> TodayAsync(CancellationToken token) => ReadAsync(new(Today, Today), token);

    public static byte[] Csv(WebsiteUsageReport report)
    {
        var csv = new StringBuilder("Date,EstimatedVisitors,ActivePatients,Coverage,Timezone,GeneratedUtc\r\n");
        foreach (var row in report.Rows)
            csv.Append(row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EstimatedVisitors?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActivePatients?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Coverage == WebsiteUsageCoverage.Partial && row.IsToday ? "Partial (today incomplete)" : row.CoverageLabel).Append(',')
                .Append("Asia/Manila,").Append(report.GeneratedUtc.ToString("O", CultureInfo.InvariantCulture)).Append("\r\n");
        return Encoding.UTF8.GetBytes(csv.ToString());
    }
}
