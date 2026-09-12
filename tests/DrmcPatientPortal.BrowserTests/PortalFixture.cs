using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.BrowserTests;

[CollectionDefinition("Portal")]
public sealed class PortalCollection : ICollectionFixture<PortalFixture> { }

public sealed class PortalFixture : IAsyncLifetime
{
    public string Root { get; } = FindRoot();
    public string Url { get; private set; } = "";
    public IPlaywright Playwright { get; private set; } = null!;
    public string Artifacts => Path.Combine(Root, "output", "playwright");
    private Process? server;
    private readonly string state = Path.Combine(Path.GetTempPath(), "drmc-browser-" + Guid.NewGuid().ToString("N"));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DrmcPatientPortal.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Run from the repository checkout.");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(Artifacts);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Url = $"http://127.0.0.1:{port}";
        var app = Path.Combine(Root, "src", "DrmcPatientPortal");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = app, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Run the referenced build directly; this also works with dotnet test -c Release.
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "DrmcPatientPortal.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = Url;
        start.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={Path.Combine(state, "portal.db")}";
        start.Environment["DataProtection__KeyRingPath"] = Path.Combine(state, "keys");
        start.Environment["PatientDocuments__RootPath"] = Path.Combine(state, "documents");
        start.Environment["PatientDocuments__TemporaryPath"] = Path.Combine(state, "uploads");
        server = Process.Start(start)!;
        var output = server.StandardOutput.ReadToEndAsync();
        var errors = server.StandardError.ReadToEndAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 90; attempt++)
        {
            if (server.HasExited)
                throw new InvalidOperationException(await output + await errors);
            try
            {
                if ((await client.GetAsync(Url)).IsSuccessStatusCode)
                {
                    // Development's first seed creates appointments late in its transaction.
                    // Supply a deterministic summary fixture without changing production seeding.
                    using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseSqlite(start.Environment["ConnectionStrings__DefaultConnection"] + ";Pooling=False").Options);
                    if (!await db.TriageIntakes.AnyAsync())
                    {
                        var appointment = await db.Appointments.OrderBy(a => a.Id).FirstAsync();
                        db.TriageIntakes.Add(new TriageIntake { AppointmentId = appointment.Id,
                            PatientUserId = appointment.PatientUserId!, ChiefComplaint = "Test follow-up",
                            AcuityLevel = TriageAcuity.Routine, SubmittedAt = DateTime.UtcNow });
                        await db.SaveChangesAsync();
                    }
                    Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                    return;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("Isolated portal did not start.");
    }

    public async Task DisposeAsync()
    {
        Playwright?.Dispose();
        if (server is { HasExited: false })
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
        server?.Dispose();
        // Only this fixture's unique temporary directory is removed.
        if (Directory.Exists(state)) Directory.Delete(state, recursive: true);
    }

    public IBrowserType Engine(string name) => name switch
    {
        "webkit" => Playwright.Webkit, "firefox" => Playwright.Firefox, _ => Playwright.Chromium
    };

    public BrowserNewContextOptions Context(string device, string culture = "en")
    {
        var options = device switch
        {
            "iphone" => new BrowserNewContextOptions(Playwright.Devices["iPhone 13"]),
            "pixel" => new BrowserNewContextOptions(Playwright.Devices["Pixel 5"]),
            "small" => new BrowserNewContextOptions(Playwright.Devices["iPhone SE"])
            { ViewportSize = new() { Width = 320, Height = 568 }, ScreenSize = new() { Width = 320, Height = 568 } },
            "landscape" => new BrowserNewContextOptions(Playwright.Devices["iPhone 13 landscape"]),
            _ => new BrowserNewContextOptions { ViewportSize = new() { Width = int.Parse(device), Height = 1024 } }
        };
        if (device == "1440") options.ViewportSize = new() { Width = 1440, Height = 900 };
        if (device == "1920") options.ViewportSize = new() { Width = 1920, Height = 1080 };
        options.BaseURL = Url;
        options.Locale = culture;
        return options;
    }

    public static async Task SignIn(IPage page, bool empty = false)
    {
        await page.GotoAsync("/Identity/Account/Login");
        await page.Locator("input[name='Input.Email']").FillAsync(empty ? "juan@drmc.doh.gov.ph" : "patient@drmc.doh.gov.ph");
        await page.Locator("input[name='Input.Password']").FillAsync(empty ? "J@uan2026" : "P@tient2026");
        await page.Locator("#login-submit").ClickAsync();
        await page.WaitForURLAsync("**/Patient/Home");
    }
}
