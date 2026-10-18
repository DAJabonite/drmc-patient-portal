using System.Globalization;
using System.Security.Claims;
using DrmcPatientPortal;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Areas.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using System.Net;

if (args is ["--ocr-worker"])
{
    Environment.ExitCode = await OcrWorker.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<WebsiteUsageOptions>().Bind(builder.Configuration.GetSection(WebsiteUsageOptions.Section));
builder.Services.AddSingleton<WebsiteUsageHealth>();
builder.Services.AddScoped<WebsiteUsageStore>();
builder.Services.AddScoped<WebsiteUsageReports>();
builder.Services.AddSingleton<WebsiteUsageCollector>();
builder.Services.AddHostedService<WebsiteUsageWorker>();
builder.Services.AddOptions<PortalOptions>().Bind(builder.Configuration.GetSection("Portal")).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<PortalOptions>, PortalOptionsValidator>();
builder.Services.AddSingleton<AccountLinks>();
builder.Services.AddOptions<AccountPrivacyOptions>().Bind(builder.Configuration.GetSection("AccountPrivacy"))
    .Validate(x => x.MinimumResponseMilliseconds is >= 250 and <= 10000, "Account response duration must be 250–10000 ms.").ValidateOnStart();
builder.Services.AddSingleton<AccountPrivacy>();
builder.Services.AddOptions<AccountEmailOptions>().Bind(builder.Configuration.GetSection("AccountEmail"))
    .Validate(x => x.MaximumAttempts is >= 1 and <= 10 && x.LifetimeMinutes is >= 1 and <= 1440 && x.RetentionDays is >= 1 and <= 30,
        "Account email retry, expiry, or retention settings are outside the supported bounds.").ValidateOnStart();
builder.Services.AddSingleton<IAccountEmailQueue, AccountEmailQueue>();
builder.Services.AddSingleton<AccountEmailDispatcher>();
builder.Services.AddOptions<OutboundNotificationOptions>().Bind(builder.Configuration.GetSection(OutboundNotificationOptions.Section))
    .Validate(x => x.MaximumAttempts is >= 1 and <= 10 && x.LifetimeMinutes is >= 1 and <= 1440 && x.RetentionDays is >= 1 and <= 30,
        "Outbound notification retry, expiry, or retention settings are outside the supported bounds.").ValidateOnStart();
builder.Services.AddSingleton<IOutboundNotificationQueue, OutboundNotificationQueue>();
builder.Services.AddSingleton<OutboundNotificationDispatcher>();
builder.Services.AddHostedService<OutboundNotificationWorker>();
builder.Services.AddOptions<RedStarOptions>().Bind(builder.Configuration.GetSection("RedStar"))
    .Validate(x => x.MaximumAttempts is >= 1 and <= 10, "Appointment reminder attempts must be 1–10.").ValidateOnStart();
builder.Services.AddScoped<RedStarPatientService>();
builder.Services.AddScoped<RedStarImports>();
builder.Services.AddSingleton<RedStarReminderService>();
builder.Services.AddHostedService<RedStarReminderWorker>();
builder.Services.AddOptions<PortalRateLimitOptions>().Bind(builder.Configuration.GetSection("RateLimits"))
    .Validate(x => x.AuthenticationPerIp > 0 && x.AuthenticationPerAccount > 0 && x.EmailPerIp > 0 && x.EmailPerAccount > 0 &&
        x.ScansPerIp > 0 && x.ScansPerUser > 0 && x.ConcurrentScans > 0 && x.PdfPerUser > 0, "All rate limits must be positive.").ValidateOnStart();
builder.Services.AddSingleton<PortalRequestLimiter>();
builder.Services.AddSingleton<PortalSessionStore>();
builder.Services.AddScoped<PortalCookieEvents>();
builder.Services.AddOptions<OcrOptions>().Bind(builder.Configuration.GetSection("Ocr"))
    .Validate(x => x.IsValid(), "OCR limits require 1–4 workers, 0–8 queued jobs, at most 5 seconds queue wait and 10 seconds execution.").ValidateOnStart();
builder.Services.AddSingleton<IOcrProcessRunner, OcrProcessRunner>();
builder.Services.AddSingleton<OcrRuntime>();
builder.Services.AddHostedService(services => services.GetRequiredService<OcrRuntime>());
builder.Services.AddHostedService<AccountEmailWorker>();

var trustedProxies = builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        // Also constrain forwarding middleware installed by the host or environment settings.
        options.ForwardedHeaders = trustedProxies.Length > 0
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in trustedProxies) options.KnownProxies.Add(IPAddress.Parse(address));
    });

// Localization: Trilingual support for English (en), Filipino (fil), and Cebuano (ceb)
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Extended ApplicationUser with Identity & 2FA support
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    // Email confirmation defaults on; set Identity:RequireConfirmedAccount=false only for isolated local review.
    var requireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", true);
    options.SignIn.RequireConfirmedAccount = requireConfirmedAccount;
    options.SignIn.RequireConfirmedEmail = requireConfirmedAccount;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Tokens.AuthenticatorTokenProvider = SingleUseAuthenticatorProvider.ProviderName;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserStore<ResilientUserStore>()
    .AddRoleStore<ResilientRoleStore>()
    .AddTokenProvider<SingleUseAuthenticatorProvider>(SingleUseAuthenticatorProvider.ProviderName)
    .AddUserValidator<UserTextLengthValidator>();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.OnRefreshingPrincipal = PortalCookieEvents.PreserveClaims;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.EventsType = typeof(PortalCookieEvents);
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
    builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();
}
else
{
    builder.Services.AddOptions<EmailDeliveryOptions>()
        .Bind(builder.Configuration.GetSection(EmailDeliveryOptions.SectionName))
        .Validate(x => !string.IsNullOrWhiteSpace(x.Host) && x.Port > 0 && !string.IsNullOrWhiteSpace(x.FromAddress), "SMTP configuration is required in Production.")
        .ValidateOnStart();
    builder.Services.AddOptions<SmsDeliveryOptions>()
        .Bind(builder.Configuration.GetSection(SmsDeliveryOptions.SectionName))
        .Validate(x => Uri.TryCreate(x.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(x.ApiKey), "An HTTPS SMS webhook and API key are required in Production.")
        .ValidateOnStart();
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
    builder.Services.AddHttpClient<ISmsSender, HttpSmsSender>();
}

// Local vector QR codes for authenticator enrollment.
builder.Services.AddSingleton<IQrCodeService, QrCodeService>();

// PHI and security access audit logging service
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.Configure<PatientDocumentStorageOptions>(builder.Configuration.GetSection("PatientDocuments"));
builder.Services.Configure<PatientResultsOptions>(builder.Configuration.GetSection(PatientResultsOptions.SectionName));
builder.Services.Configure<PatientRegistrationOptions>(builder.Configuration.GetSection(PatientRegistrationOptions.Section));
builder.Services.Configure<StaffInvitationOptions>(builder.Configuration.GetSection(StaffInvitationOptions.Section));
builder.Services.AddScoped<StaffInvitationActivator>();
builder.Services.AddScoped<FirstAdminSignup>();
builder.Services.AddScoped<PatientDocumentStorage>();
builder.Services.AddScoped<IPatientDocumentStorage>(services => services.GetRequiredService<PatientDocumentStorage>());
builder.Services.Configure<LabReportStorageOptions>(builder.Configuration.GetSection("LabReports"));
builder.Services.AddScoped<ILabReportStorage, LabReportStorage>();
builder.Services.AddHostedService<TemporaryDocumentCleanupService>();

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
if (!Path.IsPathRooted(keyRingPath)) keyRingPath = Path.Combine(builder.Environment.ContentRootPath, keyRingPath);
var protectKeysWithDpapi = builder.Configuration.GetValue<bool>("DataProtection:ProtectKeysWithDpapi");
await KeyRingProtection.ConfigureAsync(builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath)).SetApplicationName("DRMC.PatientPortal"), keyRingPath, protectKeysWithDpapi);
builder.Services.AddSingleton(sp => new RedStarPhiProtection(sp.GetRequiredService<IDataProtectionProvider>(), keyRingPath));

// Offline local OCR ID extraction service
builder.Services.AddSingleton<IIdDocumentExtractionService, TesseractIdDocumentExtractionService>();

// Session state supports registration upload binding and other short-lived workflows.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization(AdminPolicies.Register);
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, StaffAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AdminAccessScope>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminAuthorizationResultHandler>();
builder.Services.AddScoped<AdminWrites>();
builder.Services.AddSingleton<ImportStaging>();
builder.Services.AddScoped<ImportWorkflow>();
builder.Services.AddSingleton<ImportLeases>();
builder.Services.AddHostedService<ImportWorker>();
builder.Services.AddControllersWithViews(options => options.Conventions.Add(new AdminAreaConvention()))
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(SharedResource));
    });

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Manage/GenerateRecoveryCodes",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(EnabledTwoFactorPageFilter))));
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/LoginWithRecoveryCode",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(PendingTwoFactorPageFilter))));
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Manage/Disable2fa",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(EnabledTwoFactorPageFilter))));
    // The local ConfirmEmail page validates its token and renders branded recovery with HTTP 400.
    foreach (var page in new[] { "/Account/ResetPassword", "/Account/ConfirmEmailChange" })
        options.Conventions.AddAreaPageApplicationModelConvention("Identity", page,
            model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(IdentityLinkPageFilter))));
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Manage/DeletePersonalData",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(DeletePersonalDataPageFilter))));
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Logout",
        model => model.Filters.Add(new LocalReturnUrlPageFilter()));
    foreach (var page in new[] { "/Account/ExternalLogin", "/Account/Manage/ExternalLogins" })
        options.Conventions.AddAreaPageApplicationModelConvention("Identity", page,
            model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(ExternalLoginPageFilter))));
});

var app = builder.Build();
await using (var protectionScope = app.Services.CreateAsyncScope())
{
    var protectionDb = protectionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (args.SequenceEqual(new[] { "redstar-protection", "upgrade" }))
    {
        await RedStarProtectionUpgrade.RunAsync(protectionDb);
        app.Logger.LogInformation("Private registry protection upgrade verified and complete.");
        return;
    }
    await RedStarProtectionUpgrade.VerifyReadyAsync(protectionDb);
    await PatientDocumentProtectionUpgrade.RunAsync(protectionDb,
        protectionScope.ServiceProvider.GetRequiredService<PatientDocumentStorage>(), app.Environment);
    app.Logger.LogInformation("Patient attachment protection verified; startup upgrade complete.");
}
if (trustedProxies.Length > 0) app.UseForwardedHeaders();
if (!protectKeysWithDpapi && !app.Environment.IsDevelopment())
    app.Logger.LogWarning("Data Protection keys are unprotected at rest. See the README Key Protection and Recovery section to choose and configure key protection before deployment.");
await AdminBootstrap.InitializeAsync(app.Services, app.Configuration);

// Fixtures require explicit opt-in and an already migrated, empty business schema.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    DbInitializer.Initialize(db, userManager, app.Configuration);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Equals(Microsoft.AspNetCore.Builder.MigrationsEndPointOptions.DefaultPath) &&
            (!HttpMethods.IsPost(context.Request.Method) || !context.Request.HasFormContentType))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Submit a form with the database context to apply development migrations.");
            return;
        }

        await next();
    });
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (AuditLogPersistenceException) when (!context.Response.HasStarted)
    {
        context.Response.Redirect("/Home/ServiceUnavailable");
    }
    catch (Exception error) when (!context.Response.HasStarted &&
        (error is SqlException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException || error.InnerException is SqlException))
    {
        app.Logger.LogError("Database request failed ({FailureType}).", error.GetType().Name);
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("The service is temporarily unavailable. Please try again later.");
    }
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; font-src 'self' data:; frame-ancestors 'self'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseRouting();

// Request Localization: en (Default), fil, ceb
var supportedCultures = new[] { "en", "fil", "ceb" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("en")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new CookieRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new AcceptLanguageHeaderRequestCultureProvider()
};

app.UseRequestLocalization(localizationOptions);

app.UseSession();

app.UseAuthentication();
app.UseMiddleware<PortalRequestControls>();
app.UseAuthorization();
app.UseMiddleware<WebsiteUsageMiddleware>();

app.MapStaticAssets();

app.MapAreaControllerRoute(name: "admin", areaName: "Admin", pattern: "Admin/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

AdminEndpointGuard.Validate(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));

app.Run();

public partial class Program;
