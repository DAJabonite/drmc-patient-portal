using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

// Patient-facing Radiology page, navigation placement and the PatientResults:ShowFullResults switch (D4).
[Collection(PortalCollection.Name)]
public sealed class RadiologyPatientTests(PortalFactory factory)
{
    private const string Released = "DRMC-RAD-2026-0301", Amended = "DRMC-RAD-2025-0188",
        Held = "DRMC-RAD-2026-0342", Draft = "DRMC-RAD-2026-0347", Pending = "DRMC-RAD-2026-0349";

    // One switched-on host per test run; it shares the factory's database.
    private static readonly ConditionalWeakTable<PortalFactory, WebApplicationFactory<Program>> OnHosts = new();
    private WebApplicationFactory<Program> On =>
        OnHosts.GetValue(factory, f => f.WithWebHostBuilder(b => b.UseSetting("PatientResults:ShowFullResults", "true")));

    private static async Task<HttpClient> PatientAsync<T>(WebApplicationFactory<T> host, string email) where T : class
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await client.SignInSuccessfullyAsync(email, PortalFactory.Password);
        return client;
    }

    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<RadiologyStudy> StudyAsync(string accession) =>
        Db(db => db.RadiologyStudies.AsNoTracking().SingleAsync(s => s.AccessionNumber == accession));

    private static string Section(string html, string startMarker)
    {
        var start = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker not found: {startMarker}");
        var end = html.IndexOf("</nav>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    private static string MainContent(string html) =>
        html[html.IndexOf("<main", StringComparison.Ordinal)..html.IndexOf("</main>", StringComparison.Ordinal)];

    private static string MenuSheet(string html)
    {
        var start = html.IndexOf("id=\"mobileUtilityMenu\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        return html[start..];
    }

    [Fact]
    public async Task Radiology_tab_is_for_signed_in_patients_and_Malasakit_moves_to_the_menu()
    {
        var anonymous = await factory.CreatePortalClient().GetStringAsync("/");
        foreach (var bar in new[] { Section(anonymous, "class=\"navbar navbar-drmc"), Section(anonymous, "class=\"mobile-tab-bar") })
        {
            Assert.DoesNotContain("href=\"/Patient/Radiology\"", bar);
            Assert.DoesNotContain("href=\"/Malasakit\"", bar);
        }
        Assert.Contains("href=\"/Malasakit\"", MenuSheet(anonymous));

        var patient = await PatientAsync(factory, PortalFactory.PrimaryEmail);
        var signedIn = await patient.GetStringAsync("/Patient/Home");
        foreach (var bar in new[] { Section(signedIn, "class=\"navbar navbar-drmc"), Section(signedIn, "class=\"mobile-tab-bar") })
        {
            Assert.Contains("href=\"/Patient/Radiology\"", bar);
            Assert.DoesNotContain("href=\"/Malasakit\"", bar);
        }
        Assert.Contains("href=\"/Malasakit\"", MenuSheet(signedIn));

        var radiology = await patient.GetStringAsync("/Patient/Radiology");
        Assert.Matches("class=\"nav-link active\" aria-current=\"page\" href=\"/Patient/Radiology\"", radiology);
        Assert.Matches("class=\"mobile-tab-item active\" aria-current=\"page\" href=\"/Patient/Radiology\"", radiology);
        var malasakit = await patient.GetStringAsync("/Malasakit");
        Assert.Matches("class=\"mobile-more-link active\" aria-current=\"page\" href=\"/Malasakit\"", malasakit);
    }

    [Theory]
    [InlineData("/Patient/Radiology")]
    [InlineData("/Patient/Radiology/Details/1")]
    public async Task Radiology_pages_require_sign_in(string path)
    {
        var response = await factory.CreatePortalClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Patient_list_shows_own_studies_statuses_and_filters()
    {
        var client = await PatientAsync(factory, PortalFactory.PrimaryEmail);
        var html = await client.GetStringAsync("/Patient/Radiology");
        foreach (var accession in new[] { Released, Amended, Held, Draft, Pending }) Assert.Contains(accession, html);
        Assert.Contains("data-testid=\"radiology-summary\"", html);
        Assert.Matches(@"<strong>2</strong> ready for claiming", html);
        Assert.Contains("data-testid=\"radiology-prep-guide\"", html);
        Assert.Contains("href=\"/Directory/Department/Radiology\"", html);
        foreach (var guide in RadiologyPreparation.Guides) Assert.Contains(guide.Title, html);

        var mri = await client.GetStringAsync("/Patient/Radiology?modality=MRI");
        Assert.Contains(Held, mri);
        Assert.DoesNotContain(Released, mri);

        var search = await client.GetStringAsync("/Patient/Radiology?search=ABDOMEN");
        Assert.Contains(Amended, search);
        Assert.DoesNotContain(Released, search);

        var noMatch = await client.GetStringAsync("/Patient/Radiology?search=" + new string('z', 500));
        Assert.Contains("data-testid=\"radiology-no-match\"", noMatch);

        var badRange = await client.GetStringAsync("/Patient/Radiology?dateRange=custom&startDate=2026-02-01&endDate=2026-01-01");
        Assert.Contains("The start date must be on or before the end date.", badRange);

        var empty = await PatientAsync(factory, PortalFactory.EmptyEmail);
        Assert.Contains("data-testid=\"radiology-empty\"", await empty.GetStringAsync("/Patient/Radiology"));
    }

    [Fact]
    public async Task Another_patient_gets_not_found_for_every_study()
    {
        var ids = await Db(db => db.RadiologyStudies.Where(s => s.Patient.PortalUser!.Email == PortalFactory.PrimaryEmail).Select(s => s.Id).ToListAsync());
        Assert.NotEmpty(ids);
        foreach (var host in new WebApplicationFactory<Program>[] { factory, On })
        {
            var other = await PatientAsync(host, PortalFactory.EmptyEmail);
            Assert.DoesNotContain("/Patient/Radiology/Details/", await other.GetStringAsync("/Patient/Radiology"));
            foreach (var id in ids)
            {
                var response = await other.GetAsync($"/Patient/Radiology/Details/{id}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("DRMC-RAD-", await response.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact]
    public void Switch_is_off_by_default()
    {
        Assert.False(factory.Services.GetRequiredService<IOptions<PatientResultsOptions>>().Value.ShowFullResults);
        Assert.True(On.Services.GetRequiredService<IOptions<PatientResultsOptions>>().Value.ShowFullResults);
    }

    [Fact]
    public async Task With_switch_off_details_show_availability_only()
    {
        var client = await PatientAsync(factory, PortalFactory.PrimaryEmail);
        foreach (var accession in new[] { Released, Amended, Held, Draft, Pending })
        {
            var study = await StudyAsync(accession);
            var html = await client.GetStringAsync($"/Patient/Radiology/Details/{study.Id}");
            Assert.Contains(accession, html);
            Assert.DoesNotContain("data-testid=\"radiology-report\"", html);
            Assert.DoesNotContain("Summary in plain language", html);
            Assert.DoesNotContain("FIXTURE-", html);
            foreach (var text in new[] { study.Findings, study.Impression, study.PlainLanguageSummary, study.InternalNotes, study.AmendmentNote, study.ClinicalIndication })
                if (!string.IsNullOrWhiteSpace(text)) Assert.DoesNotContain(text, html);
            Assert.Contains("Questions about your report", html);
            Assert.Contains("The patient portal does not show imaging pictures.", html);
            Assert.Contains(ClinicalDepartments.Radiology.LocalExtension, html);
            var ready = accession is Released or Amended;
            Assert.Equal(ready, html.Contains("data-testid=\"radiology-status-ready\""));
            Assert.Equal(!ready, html.Contains("data-testid=\"radiology-status-pending\""));
            Assert.Equal(accession == Amended, html.Contains("data-testid=\"radiology-amended\""));
        }
    }

    [Fact]
    public async Task With_switch_on_only_released_reports_are_shown_and_internal_notes_never()
    {
        var client = await PatientAsync(On, PortalFactory.PrimaryEmail);

        var released = await StudyAsync(Released);
        var html = await client.GetStringAsync($"/Patient/Radiology/Details/{released.Id}");
        var report = html[html.IndexOf("data-testid=\"radiology-report\"", StringComparison.Ordinal)..];
        var order = new[] { "Clinical indication", "Technique", "Comparison", "Findings", "Impression", "Summary in plain language" }
            .Select(h => report.IndexOf(h, StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Contains(released.Findings, html);
        Assert.Contains(released.Impression, html);
        Assert.Contains(released.PlainLanguageSummary, html);
        Assert.Contains("written for your doctor", html);

        var amended = await StudyAsync(Amended);
        html = await client.GetStringAsync($"/Patient/Radiology/Details/{amended.Id}");
        Assert.Contains(amended.Findings, html);
        Assert.Contains(amended.AmendmentNote, html);
        Assert.Contains("data-testid=\"radiology-amended\"", html);
        Assert.DoesNotContain("Summary in plain language", html);

        foreach (var accession in new[] { Held, Draft, Pending })
        {
            var study = await StudyAsync(accession);
            html = await client.GetStringAsync($"/Patient/Radiology/Details/{study.Id}");
            Assert.DoesNotContain("data-testid=\"radiology-report\"", html);
            Assert.DoesNotContain("FIXTURE-", html);
            Assert.Contains("data-testid=\"radiology-status-pending\"", html);
        }

        var list = await client.GetStringAsync("/Patient/Radiology");
        Assert.DoesNotContain("FIXTURE-", list);
        foreach (var accession in new[] { Released, Amended, Held, Draft, Pending })
        {
            var notes = (await StudyAsync(accession)).InternalNotes;
            if (notes.Length > 0) Assert.DoesNotContain(notes, html + list);
        }
    }

    [Fact]
    public async Task Viewing_a_study_is_audited()
    {
        var study = await StudyAsync(Released);
        var before = await Db(db => db.AuditLogs.CountAsync(a => a.Action == "VIEW_RADIOLOGY_REPORT" && a.Resource == $"RadiologyStudy/{study.Id}"));
        var client = await PatientAsync(factory, PortalFactory.PrimaryEmail);
        await client.GetStringAsync($"/Patient/Radiology/Details/{study.Id}");
        var after = await Db(db => db.AuditLogs.CountAsync(a => a.Action == "VIEW_RADIOLOGY_REPORT" && a.Resource == $"RadiologyStudy/{study.Id}"));
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Lab_values_follow_the_switch_and_clinical_notes_stay_staff_only()
    {
        var labs = await Db(db => db.LabResults.AsNoTracking().Include(l => l.Items)
            .Where(l => l.Patient.PortalUser!.Email == PortalFactory.PrimaryEmail).ToListAsync());
        var now = PatientResultsDisclosure.ManilaNow;
        var released = labs.First(l => PatientResultsDisclosure.IsReleased(l, now) && l.Items.Count > 0 && l.ResultSummary.Length > 0);
        var unreleased = labs.First(l => !PatientResultsDisclosure.IsReleased(l, now));

        var off = await PatientAsync(factory, PortalFactory.PrimaryEmail);
        var on = await PatientAsync(On, PortalFactory.PrimaryEmail);

        var offHtml = await off.GetStringAsync($"/Patient/LabResults/Details/{released.Id}");
        Assert.DoesNotContain("data-testid=\"lab-result-values\"", offHtml);
        Assert.DoesNotContain(released.ResultSummary, offHtml);

        var onHtml = await on.GetStringAsync($"/Patient/LabResults/Details/{released.Id}");
        Assert.Contains("data-testid=\"lab-result-values\"", onHtml);
        Assert.Contains(released.ResultSummary, onHtml);
        foreach (var item in released.Items)
        {
            Assert.Matches(Regex.Escape(item.ParameterName) + @"</th>\s*<td[^>]*>" + Regex.Escape(item.Value) + "</td>", onHtml);
            Assert.Contains(item.ReferenceRange, onHtml);
        }
        Assert.Contains(released.Items.First().Flag.ToString(), onHtml);

        // With the block absent, the switched-on page is otherwise the same as the switched-off page.
        var stripped = Regex.Replace(MainContent(onHtml), @"\s*<section class=""mb-4"" aria-labelledby=""labValuesHeading"".*?</section>", "", RegexOptions.Singleline);
        Assert.Equal(Regex.Replace(MainContent(offHtml), @"\s+", " "), Regex.Replace(stripped, @"\s+", " "));

        var pendingHtml = await on.GetStringAsync($"/Patient/LabResults/Details/{unreleased.Id}");
        Assert.DoesNotContain("data-testid=\"lab-result-values\"", pendingHtml);

        foreach (var lab in labs.Where(l => l.ClinicalNotes.Length > 0))
        {
            Assert.DoesNotContain(lab.ClinicalNotes, await on.GetStringAsync($"/Patient/LabResults/Details/{lab.Id}"));
            Assert.DoesNotContain(lab.ClinicalNotes, await off.GetStringAsync($"/Patient/LabResults/Details/{lab.Id}"));
        }
    }

    [Fact]
    public void Tracked_settings_never_switch_full_results_on()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "DrmcPatientPortal.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var settings = Directory.GetFiles(Path.Combine(root!, "src", "DrmcPatientPortal"), "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Append(Path.Combine(root!, "src", "DrmcPatientPortal", "Properties", "launchSettings.json")).ToArray();
        Assert.NotEmpty(settings);
        foreach (var file in settings)
            Assert.DoesNotMatch(@"ShowFullResults""?\s*[:=]\s*""?true", File.ReadAllText(file));
        Assert.Matches(@"""ShowFullResults""\s*:\s*false", File.ReadAllText(Path.Combine(root!, "src", "DrmcPatientPortal", "appsettings.json")));
    }
}
