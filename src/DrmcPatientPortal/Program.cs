using System.Globalization;
using DrmcPatientPortal;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Localization: Trilingual support for English (en), Filipino (fil), and Cebuano (ceb)
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Extended ApplicationUser with Identity & 2FA support
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserValidator<UserTextLengthValidator>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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
builder.Services.AddScoped<IPatientDocumentStorage, PatientDocumentStorage>();
builder.Services.AddHostedService<TemporaryDocumentCleanupService>();

var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
if (!Path.IsPathRooted(keyRingPath)) keyRingPath = Path.Combine(builder.Environment.ContentRootPath, keyRingPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath)).SetApplicationName("DRMC.PatientPortal");

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

builder.Services.AddControllersWithViews()
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
    foreach (var page in new[] { "/Account/ResetPassword", "/Account/ConfirmEmail", "/Account/ConfirmEmailChange" })
        options.Conventions.AddAreaPageApplicationModelConvention("Identity", page,
            model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(IdentityLinkPageFilter))));
    options.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Manage/DeletePersonalData",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(DeletePersonalDataPageFilter))));
    foreach (var page in new[] { "/Account/ExternalLogin", "/Account/Manage/ExternalLogins" })
        options.Conventions.AddAreaPageApplicationModelConvention("Identity", page,
            model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(typeof(ExternalLoginPageFilter))));
});

var app = builder.Build();

// Seed the database with development/review patient data (safe: only in Development).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    DbInitializer.Initialize(db, userManager);
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

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (AuditLogPersistenceException) when (!context.Response.HasStarted)
    {
        context.Response.Redirect("/Home/ServiceUnavailable");
    }
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self' data:; frame-ancestors 'self'; base-uri 'self'; form-action 'self'";
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
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
