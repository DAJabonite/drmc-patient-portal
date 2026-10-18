using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text;
using DrmcPatientPortal.Areas.Admin.Models;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

public sealed class WebsiteUsageUnitTests
{
    [Fact]
    public void Filters_handle_month_ends_leap_days_and_inclusive_limits()
    {
        var today = new DateOnly(2028, 3, 10);
        Assert.True(new WebsiteUsageFilter { Month = "2028-02" }.TryResolve(today, out var leap, out _));
        Assert.Equal(new DateOnly(2028, 2, 29), leap.End);
        Assert.True(new WebsiteUsageFilter().TryResolve(today, out var current, out _));
        Assert.Equal(today, current.End);
        var filter = new WebsiteUsageFilter { Mode = WebsiteUsagePeriod.Range, StartDate = today.AddDays(-365), EndDate = today };
        Assert.True(filter.TryResolve(today, out _, out _));
        filter.StartDate = today.AddDays(-366);
        Assert.False(filter.TryResolve(today, out _, out _));
        filter.StartDate = today.AddDays(1);
        Assert.False(filter.TryResolve(today, out _, out _));
        Assert.False(new WebsiteUsageFilter { Mode = WebsiteUsagePeriod.Day, Day = today.AddDays(1) }.TryResolve(today, out _, out _));
        Assert.False(new WebsiteUsageFilter { Mode = (WebsiteUsagePeriod)99 }.TryResolve(today, out _, out _));
    }

    [Theory]
    [InlineData("2028-2")]
    [InlineData("2028-13")]
    [InlineData("2029-01")]
    [InlineData("invalid")]
    public void Invalid_or_future_months_are_rejected(string month) =>
        Assert.False(new WebsiteUsageFilter { Month = month }.TryResolve(new(2028, 3, 10), out _, out _));

    internal static DefaultHttpContext Page(string controller = "Home", string action = "Index", string? area = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Firefox/130.0";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html; charset=utf-8";
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action,
            RouteValues = new Dictionary<string, string?> { ["area"] = area } };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(descriptor), "synthetic page"));
        return context;
    }

    [Fact]
    public void Eligibility_excludes_non_pages_errors_prefetch_automation_and_privacy_signals()
    {
        Assert.True(WebsiteUsageMiddleware.Eligible(Page()));
        Assert.True(WebsiteUsageMiddleware.Eligible(Page("RedStarPatient")));
        Assert.False(WebsiteUsageMiddleware.Eligible(Page(area: "Admin")));
        Assert.False(WebsiteUsageMiddleware.Eligible(Page(area: "Identity")));
        Assert.False(WebsiteUsageMiddleware.Eligible(Page("Api")));
        Assert.False(WebsiteUsageMiddleware.Eligible(Page(action: "Error")));
        foreach (var header in new[] { "DNT", "Sec-GPC", "Purpose", "Sec-Purpose" })
        {
            var context = Page(); context.Request.Headers[header] = header is "DNT" or "Sec-GPC" ? "1" : "prefetch";
            Assert.False(WebsiteUsageMiddleware.Eligible(context));
        }
        foreach (var agent in new[] { "Googlebot", "UptimeRobot", "", "HeadlessChrome" })
        {
            var context = Page(); context.Request.Headers.UserAgent = agent;
            Assert.False(WebsiteUsageMiddleware.Eligible(context));
        }
        foreach (var status in new[] { 302, 403, 404, 500 })
        {
            var context = Page(); context.Response.StatusCode = status;
            Assert.False(WebsiteUsageMiddleware.Eligible(context));
        }
        var download = Page(); download.Response.ContentType = "application/pdf";
        Assert.False(WebsiteUsageMiddleware.Eligible(download));
        var head = Page(); head.Request.Method = "HEAD";
        Assert.False(WebsiteUsageMiddleware.Eligible(head));
        var asset = Page(); asset.SetEndpoint(null);
        Assert.False(WebsiteUsageMiddleware.Eligible(asset));
    }

    [Fact]
    public void Manila_midnight_and_missing_chart_days_are_preserved()
    {
        Assert.Equal(new DateOnly(2028, 2, 29), WebsiteUsageClock.Day(DateTimeOffset.Parse("2028-02-29T15:59:59Z")));
        Assert.Equal(new DateOnly(2028, 3, 1), WebsiteUsageClock.Day(DateTimeOffset.Parse("2028-02-29T16:00:00Z")));
        var day = new DateOnly(2028, 3, 1);
        var report = new WebsiteUsageReport(new(day, day.AddDays(2)), DateTimeOffset.UtcNow, false,
            [new(day, 0, 0, WebsiteUsageCoverage.Complete, false), new(day.AddDays(1), null, null, WebsiteUsageCoverage.NotCollected, false),
             new(day.AddDays(2), 2, 1, WebsiteUsageCoverage.Partial, false)]);
        Assert.Equal(2, report.VisitorDays);
        Assert.Equal(0d, report.AverageVisitors);
        Assert.Equal(2, report.ChartSegments(false).Count);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Contains("2028-03-02,,,Not collected,Asia/Manila,", Encoding.UTF8.GetString(WebsiteUsageReports.Csv(report)));
            Assert.Contains("2028-03-03,2,1,Partial,Asia/Manila,", Encoding.UTF8.GetString(WebsiteUsageReports.Csv(report)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private sealed class UnavailableScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Sensitive request value must not be logged");
    }
    internal sealed class CapturingLogger : ILogger<WebsiteUsageCollector>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Collection_failures_preserve_responses_and_sanitize_logs()
    {
        var health = new WebsiteUsageHealth(); var logger = new CapturingLogger();
        var context = Page(); context.Response.Body = new MemoryStream();
        await context.Response.WriteAsync("Existing page response");
        var collector = new WebsiteUsageCollector(new UnavailableScopes(), TimeProvider.System, health, logger);
        await collector.CollectAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("Existing page response", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.NotNull(health.TakeFailure());
        Assert.Null(health.TakeFailure());
        Assert.Single(logger.Messages);
        Assert.DoesNotContain("Sensitive", logger.Messages[0]);
    }
}

[Collection(PortalCollection.Name)]
public sealed class WebsiteUsageIntegrationTests(PortalFactory factory)
{
    private sealed class CompletionFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                try { await following(); }
                finally
                {
                    if (context.Items.TryGetValue(typeof(CompletionFilter), out var completion))
                        ((TaskCompletionSource)completion!).TrySetResult();
                }
            });
            next(app);
        };
    }
    private async Task Record(DateTimeOffset now, string address, string? account = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WebsiteUsageStore>().RecordAsync(now, address, "Synthetic Browser", account, default);
    }

    private async Task<WebsiteUsageDay?> Read(DateOnly day)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WebsiteUsageDays.AsNoTracking().SingleOrDefaultAsync(d => d.Day == day);
    }

    [Fact]
    public async Task Concurrent_collection_deduplicates_across_contexts_and_devices()
    {
        var now = DateTimeOffset.Parse("2040-01-02T00:01:00+08:00");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Record(now, "192.0.2.10", "synthetic-account-a")));
        await Record(now.AddMinutes(1), "192.0.2.11", "synthetic-account-a");
        var row = (await Read(new(2040, 1, 2)))!;
        Assert.Equal(2, row.EstimatedVisitors);
        Assert.Equal(1, row.ActivePatients);
        Assert.False(row.HasGap);
        await using var scope = factory.Services.CreateAsyncScope();
        var markers = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WebsiteUsageMarkers.Where(m => m.Day == row.Day).ToListAsync();
        Assert.Equal(3, markers.Count);
        Assert.All(markers, m => Assert.Matches("^[A-F0-9]{64}$", m.Identifier));
        Assert.DoesNotContain(markers, m => m.Identifier.Contains("synthetic-account") || m.Identifier.Contains("192.0.2"));
    }

    [Fact]
    public async Task Identifiers_rotate_daily_and_totals_are_patient_days()
    {
        var first = new DateOnly(2040, 2, 27);
        for (var offset = 0; offset < 3; offset++) await Record(WebsiteUsageClock.Start(first.AddDays(offset)).AddMinutes(1), "192.0.2.20", "synthetic-account-b");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identifiers = await db.WebsiteUsageMarkers.Where(m => m.Day >= first && m.Day <= first.AddDays(2) && m.Audience == Models.WebsiteUsageAudience.Patient)
            .Select(m => m.Identifier).ToListAsync();
        Assert.Equal(3, identifiers.Distinct().Count());
        var service = new WebsiteUsageReports(db, new TestClock(WebsiteUsageClock.Start(first.AddDays(3))), Options.Create(new WebsiteUsageOptions()));
        var report = await service.ReadAsync(new(first, first.AddDays(2)));
        Assert.Equal(3, report.PatientDays);
        Assert.All(report.Rows, r => Assert.Equal(1, r.ActivePatients));
    }

    [Fact]
    public async Task Maintenance_records_zero_days_gaps_and_purges_keys_without_losing_totals()
    {
        var day = new DateOnly(2040, 4, 10);
        var midnight = WebsiteUsageClock.Start(day);
        await Record(midnight.AddMinutes(1), "192.0.2.30");
        var key = (await Read(day))!.ProtectedKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<WebsiteUsageStore>();
            await store.MaintainAsync(midnight.AddMinutes(4), true, null, default);
            Assert.True((await Read(day))!.HasGap);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<WebsiteUsageStore>().MaintainAsync(midnight.AddDays(1).AddMinutes(4), true, null, default);
        Assert.Equal(key, (await Read(day))!.ProtectedKey);
        Assert.Equal(0, (await Read(day.AddDays(1)))!.EstimatedVisitors);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<WebsiteUsageStore>().MaintainAsync(midnight.AddDays(1).AddMinutes(5), false, null, default);
        Assert.Null((await Read(day))!.ProtectedKey);
        Assert.Equal(1, (await Read(day))!.EstimatedVisitors);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WebsiteUsageMarkers.AnyAsync(m => m.Day == day));
    }

    [Fact]
    public async Task Live_staff_roles_are_excluded_from_collection()
    {
        await StaffAccounts.AdminAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(u => u.Email == StaffAccounts.AdminEmail);
        await Record(DateTimeOffset.Parse("2040-05-01T12:00:00+08:00"), "192.0.2.40", account.Id);
        Assert.Null(await Read(new(2040, 5, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_pipeline_uses_only_trusted_proxy_addresses_and_honors_optouts(bool trusted)
    {
        var date = new DateOnly(2040, 6, trusted ? 2 : 1);
        var now = WebsiteUsageClock.Start(date).AddMinutes(1);
        using var host = factory.WithWebHostBuilder(builder =>
        {
            if (trusted) builder.UseSetting("Proxy:KnownProxies:0", "192.0.2.51");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, CompletionFilter>();
                services.AddSingleton<WebsiteUsageCollector>(sp => new(
                    sp.GetRequiredService<IServiceScopeFactory>(), new TestClock(now), sp.GetRequiredService<WebsiteUsageHealth>(),
                    sp.GetRequiredService<ILogger<WebsiteUsageCollector>>()));
            });
        });
        _ = host.Server;
        var settings = host.Services.GetRequiredService<IOptions<WebsiteUsageOptions>>().Value;
        settings.Enabled = true;
        async Task Send(string forwarded, bool optout = false)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await host.Server.SendAsync(context =>
            {
                context.Items[typeof(CompletionFilter)] = completion;
                context.Request.Method = "GET"; context.Request.Path = "/";
                context.Request.Scheme = "https"; context.Request.Host = new("localhost");
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.51");
                context.Request.Headers.UserAgent = "Mozilla/5.0 SyntheticBrowser";
                context.Request.Headers["X-Forwarded-For"] = forwarded;
                if (optout) context.Request.Headers["Sec-GPC"] = "1";
            });
            Assert.Equal(200, result.Response.StatusCode);
            // SendAsync can return when the response completes, before middleware's
            // post-response collection. Observe the entire pipeline before reading SQL.
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Send("198.51.100.1");
        await Send("198.51.100.2");
        await Send("198.51.100.3", true);
        Assert.Equal(trusted ? 2 : 1, (await Read(date))!.EstimatedVisitors);
        settings.Enabled = false;
        await Send("198.51.100.4");
        Assert.Equal(trusted ? 2 : 1, (await Read(date))!.EstimatedVisitors);
    }

    [Fact]
    public async Task Collection_deadline_cancels_a_blocked_SQL_write_without_changing_the_response()
    {
        var day = new DateOnly(2040, 6, 10); var now = WebsiteUsageClock.Start(day).AddMinutes(1);
        await Record(now, "192.0.2.60");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var held = await db.Database.BeginTransactionAsync();
        _ = await db.WebsiteUsageDays.FromSqlInterpolated($"SELECT * FROM [WebsiteUsageDays] WITH (UPDLOCK, HOLDLOCK) WHERE [Day] = {day}").SingleAsync();
        var health = new WebsiteUsageHealth(); var logger = new WebsiteUsageUnitTests.CapturingLogger();
        var collector = new WebsiteUsageCollector(factory.Services.GetRequiredService<IServiceScopeFactory>(), new TestClock(now), health, logger);
        var context = WebsiteUsageUnitTests.Page();
        var timer = Stopwatch.StartNew();
        await collector.CollectAsync(context);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4));
        Assert.Equal(200, context.Response.StatusCode);
        Assert.NotNull(health.TakeFailure());
        Assert.Single(logger.Messages);
        await held.RollbackAsync();
        Assert.Equal(1, (await Read(day))!.EstimatedVisitors);
    }

    [Fact]
    public async Task Reports_include_complete_zero_days_and_keep_partial_days_out_of_averages()
    {
        var first = new DateOnly(2040, 7, 1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.WebsiteUsageDays.AddRange(
            new WebsiteUsageDay { Day = first, CoverageStartedUtc = WebsiteUsageClock.Start(first), LastHeartbeatUtc = WebsiteUsageClock.Start(first.AddDays(1)).AddMinutes(-1) },
            new WebsiteUsageDay { Day = first.AddDays(1), EstimatedVisitors = 5, ActivePatients = 2, HasGap = true,
                CoverageStartedUtc = WebsiteUsageClock.Start(first.AddDays(1)), LastHeartbeatUtc = WebsiteUsageClock.Start(first.AddDays(2)).AddMinutes(-1) });
        await db.SaveChangesAsync();
        var report = await new WebsiteUsageReports(db, new TestClock(WebsiteUsageClock.Start(first.AddDays(3))), Options.Create(new WebsiteUsageOptions()))
            .ReadAsync(new(first, first.AddDays(2)));
        Assert.Equal(WebsiteUsageCoverage.Complete, report.Rows[0].Coverage);
        Assert.Equal(0, report.Rows[0].EstimatedVisitors);
        Assert.Equal(WebsiteUsageCoverage.Partial, report.Rows[1].Coverage);
        Assert.Equal(WebsiteUsageCoverage.NotCollected, report.Rows[2].Coverage);
        Assert.Equal(1, report.CompleteDays);
        Assert.Equal(0d, report.AverageVisitors);
        Assert.Equal(5, report.VisitorDays);
    }

    [Fact]
    public async Task Disabled_maintenance_enforces_thirteen_calendar_month_retention()
    {
        var today = new DateOnly(2040, 8, 10); var boundary = today.AddMonths(-13);
        await Record(WebsiteUsageClock.Start(boundary.AddDays(-1)), "192.0.2.70");
        await Record(WebsiteUsageClock.Start(boundary), "192.0.2.71");
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WebsiteUsageStore>().MaintainAsync(WebsiteUsageClock.Start(today).AddMinutes(5), false, null, default);
        Assert.Null(await Read(boundary.AddDays(-1)));
        Assert.Equal(1, (await Read(boundary))!.EstimatedVisitors);
        Assert.Null((await Read(boundary))!.ProtectedKey);
    }

    [Fact]
    public async Task Report_and_csv_use_identical_filters_and_exclude_missing_days()
    {
        var client = await StaffAccounts.AdminAsync(factory);
        const string query = "?Mode=Range&StartDate=2020-02-28&EndDate=2020-03-01";
        var html = await client.GetStringAsync("/Admin/WebsiteUsage" + query);
        Assert.Contains("2020-02-29", html);
        Assert.Contains("Not collected", html);
        Assert.Contains("No usage data", html);
        Assert.DoesNotContain("<script>", html);
        var response = await client.GetAsync("/Admin/WebsiteUsage/Export" + query);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.ToString());
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(4, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("2020-02-29,,,Not collected,Asia/Manila,", csv);
        Assert.DoesNotContain(PortalFactory.PrimaryEmail, csv);
        Assert.DoesNotContain("<script", csv);
        Assert.Contains("2020-02-29", await client.GetStringAsync("/Admin/WebsiteUsage?Filter.Mode=Day&Filter.Day=2020-02-29"));
        foreach (var bad in new[] { "?Mode=Day&Day=broken", "?Month=2040-01", "?Mode=invalid", "?Mode=Range&StartDate=2020-02-01&EndDate=2019-01-01",
                     "?Mode=Range&StartDate=2019-01-01&EndDate=2020-12-31" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Admin/WebsiteUsage" + bad)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Admin/WebsiteUsage/Export" + bad)).StatusCode);
        }
    }

    [Fact]
    public async Task Measured_reports_render_accessible_charts_and_matching_CSV_counts()
    {
        var day = WebsiteUsageClock.Day(DateTimeOffset.UtcNow).AddDays(-1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WebsiteUsageDays.Add(new WebsiteUsageDay { Day = day, EstimatedVisitors = 12, ActivePatients = 4,
                CoverageStartedUtc = WebsiteUsageClock.Start(day), LastHeartbeatUtc = WebsiteUsageClock.Start(day.AddDays(1)).AddMinutes(-1) });
            await db.SaveChangesAsync();
        }
        var query = $"?Mode=Range&StartDate={day:yyyy-MM-dd}&EndDate={day:yyyy-MM-dd}";
        var client = await StaffAccounts.AdminAsync(factory);
        var html = await client.GetStringAsync("/Admin/WebsiteUsage" + query);
        Assert.Contains("aria-labelledby=\"usage-svg-title usage-svg-desc\"", html);
        Assert.Contains("usage-visitor-line", html);
        Assert.Contains("usage-patient-line", html);
        Assert.Contains("scope=\"col\"", html);
        Assert.Contains("Visitor-days", html);
        Assert.Contains("Patient-days", html);
        Assert.DoesNotContain("<script>", html);
        var csv = await client.GetStringAsync("/Admin/WebsiteUsage/Export" + query);
        Assert.Contains($"{day:yyyy-MM-dd},12,4,Complete,Asia/Manila,", csv);
    }

    [Fact]
    public async Task Report_access_rechecks_admin_role_and_requires_mfa()
    {
        const string email = "usage.revoked.admin@fixtures.test";
        var client = await StaffAccounts.SignInAsync(factory, email, "Admin");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/WebsiteUsage")).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Models.ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByEmailAsync(email))!, "Admin")).Succeeded);
        }
        foreach (var path in new[] { "/Admin/WebsiteUsage", "/Admin/WebsiteUsage/Export" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);

        const string noMfa = "usage.no-mfa.admin@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, noMfa, false, "Admin");
        var passwordClient = factory.CreatePortalClient();
        await passwordClient.SignInSuccessfullyAsync(noMfa, PortalFactory.Password);
        foreach (var path in new[] { "/Admin/WebsiteUsage", "/Admin/WebsiteUsage/Export" })
            Assert.Equal(HttpStatusCode.Forbidden, (await passwordClient.GetAsync(path)).StatusCode);
    }
}
