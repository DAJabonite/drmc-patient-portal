using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Security.Claims;
using System.Text;
using DrmcPatientPortal.Areas.Admin.Services;
using DrmcPatientPortal.Controllers;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using DrmcPatientPortal.Areas.Admin.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

[Collection(PortalCollection.Name)]
public sealed class ReliabilityHardeningTests(PortalFactory factory)
{
    private sealed class MailSender : IEmailSender
    {
        public bool Fail { get; set; }
        public ConcurrentQueue<string> Bodies { get; } = new();
        public Task SendEmailAsync(string email, string subject, string body)
        {
            if (Fail) throw new IOException("Synthetic private message: TOKEN-MUST-NOT-BE-LOGGED");
            Bodies.Enqueue(body); return Task.CompletedTask;
        }
    }
    private async Task<(string Id, string Email)> Account()
    {
        var email = $"reliability.{Guid.NewGuid():N}@fixtures.test";
        await StaffAccounts.EnsureAccountAsync(factory.Services, email, false);
        using var scope = factory.Services.CreateScope();
        return ((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id, email);
    }
    private async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope(); return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
    private AccountEmailDispatcher Dispatcher(IServiceScopeFactory scopes, IDataProtectionProvider protection, TestClock clock) =>
        new(scopes, protection, Options.Create(new AccountEmailOptions()), clock, NullLogger<AccountEmailDispatcher>.Instance);

    [Fact]
    public async Task Two_separate_worker_processes_deliver_a_durable_message_under_one_sql_lease()
    {
        var account = await Account();
        await factory.Services.GetRequiredService<IAccountEmailQueue>().EnqueueAsync(account.Id, account.Email, "Process fixture", "Synthetic process lease fixture.");
        var id = await Db(db => db.AccountEmails.Where(m => m.UserId == account.Id).Select(m => m.Id).SingleAsync());
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var contentRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        Process StartWorker()
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = contentRoot };
            start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--urls"); start.ArgumentList.Add("http://127.0.0.1:0");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__DefaultConnection"] = config["ConnectionStrings:DefaultConnection"]!;
            start.Environment["DataProtection__KeyRingPath"] = config["DataProtection:KeyRingPath"]!;
            start.Environment["DataProtection__CertificatePath"] = config["DataProtection:CertificatePath"]!;
            start.Environment["DataProtection__CertificatePasswordFile"] = config["DataProtection:CertificatePasswordFile"]!;
            start.Environment["DataProtection__ProtectKeysWithDpapi"] = "false";
            start.Environment["AccountEmail__WorkerEnabled"] = "true";
            start.Environment["DevelopmentFixtures__Enabled"] = "false";
            start.Environment["AdminBootstrap__Enabled"] = "false";
            return Process.Start(start)!;
        }
        using var first = StartWorker(); using var second = StartWorker();
        // Drain redirected streams without exposing even synthetic account-message contents.
        var streams = new[] { first.StandardOutput.ReadToEndAsync(), first.StandardError.ReadToEndAsync(),
            second.StandardOutput.ReadToEndAsync(), second.StandardError.ReadToEndAsync() };
        try
        {
            var deadline = Stopwatch.StartNew(); AccountEmail? message = null;
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                message = await Db(db => db.AccountEmails.AsNoTracking().SingleAsync(m => m.Id == id));
                if (message.Status == AccountEmailStatus.Delivered) break;
                Assert.False(first.HasExited && second.HasExited, "Both worker processes stopped before delivery.");
                await Task.Delay(100);
            }
            Assert.NotNull(message); Assert.Equal(AccountEmailStatus.Delivered, message.Status);
            Assert.Equal(1, message.Attempts); Assert.Null(message.ProtectedPayload);
        }
        finally
        {
            foreach (var process in new[] { first, second })
            { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(streams);
        }
    }

    [Fact]
    public async Task Encrypted_email_queue_survives_worker_replacement_and_overlapping_leases_clear_delivered_payloads()
    {
        var account = await Account(); var sender = new MailSender(); var clock = new TestClock(DateTimeOffset.UtcNow);
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var scopes = host.Services.GetRequiredService<IServiceScopeFactory>(); var protection = host.Services.GetRequiredService<IDataProtectionProvider>();
        var queue = new AccountEmailQueue(scopes, protection, Options.Create(new AccountEmailOptions()), clock);
        await queue.EnqueueAsync(account.Id, account.Email, "Verification", "SENSITIVE-LINK-TOKEN");
        var queued = await Db(db => db.AccountEmails.AsNoTracking().SingleAsync(m => m.UserId == account.Id));
        Assert.DoesNotContain("SENSITIVE-LINK-TOKEN", Encoding.UTF8.GetString(queued.ProtectedPayload!));
        // Model a killed process: a sending lease becomes reclaimable only after its deadline.
        await Db(async db => await db.AccountEmails.Where(m => m.Id == queued.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(m => m.Status, AccountEmailStatus.Sending).SetProperty(m => m.LeaseOwner, "old-process")
                .SetProperty(m => m.LeaseExpiresUtc, (DateTimeOffset?)clock.Now.AddMinutes(2)).SetProperty(m => m.Attempts, 1)));
        var first = Dispatcher(scopes, protection, clock); var second = Dispatcher(scopes, protection, clock);
        await first.DispatchOnceAsync(); Assert.DoesNotContain(sender.Bodies, body => body == "SENSITIVE-LINK-TOKEN");
        clock.Now = clock.Now.AddMinutes(3);
        await Task.WhenAll(first.DispatchOnceAsync(), second.DispatchOnceAsync());
        Assert.Single(sender.Bodies, body => body == "SENSITIVE-LINK-TOKEN");
        var delivered = await Db(db => db.AccountEmails.AsNoTracking().SingleAsync(m => m.Id == queued.Id));
        Assert.Equal(AccountEmailStatus.Delivered, delivered.Status); Assert.Null(delivered.ProtectedPayload); Assert.Null(delivered.LeaseOwner);
        Assert.Equal(2, delivered.Attempts);
        clock.Now = clock.Now.AddDays(8); await second.CleanupAsync();
        Assert.False(await Db(db => db.AccountEmails.AnyAsync(m => m.Id == queued.Id)));
    }

    [Fact]
    public async Task Email_retries_are_bounded_and_expiry_clears_sensitive_payloads()
    {
        var account = await Account(); var clock = new TestClock(DateTimeOffset.UtcNow); var sender = new MailSender { Fail = true };
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(sender); }));
        var scopes = host.Services.GetRequiredService<IServiceScopeFactory>(); var protection = host.Services.GetRequiredService<IDataProtectionProvider>();
        var queue = new AccountEmailQueue(scopes, protection, Options.Create(new AccountEmailOptions()), clock);
        var dispatcher = Dispatcher(scopes, protection, clock);
        await queue.EnqueueAsync(account.Id, account.Email, "Reset", "RETRY-TOKEN");
        var id = await Db(db => db.AccountEmails.Where(m => m.UserId == account.Id).Select(m => m.Id).SingleAsync());
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await dispatcher.DispatchOnceAsync();
            var current = await Db(db => db.AccountEmails.AsNoTracking().SingleAsync(m => m.Id == id));
            Assert.Equal(attempt, current.Attempts); clock.Now = current.NextAttemptUtc.AddSeconds(1);
        }
        var failed = await Db(db => db.AccountEmails.AsNoTracking().SingleAsync(m => m.Id == id));
        Assert.Equal(AccountEmailStatus.Failed, failed.Status); Assert.Null(failed.ProtectedPayload);
        await dispatcher.DispatchOnceAsync(); Assert.Equal(5, await Db(db => db.AccountEmails.Where(m => m.Id == id).Select(m => m.Attempts).SingleAsync()));
        await queue.EnqueueAsync(account.Id, account.Email, "Reset", "EXPIRED-TOKEN");
        clock.Now = clock.Now.AddHours(2); await dispatcher.CleanupAsync();
        var expired = await Db(db => db.AccountEmails.SingleAsync(m => m.UserId == account.Id && m.Status == AccountEmailStatus.Expired));
        Assert.Null(expired.ProtectedPayload); Assert.NotNull(expired.CompletedUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Security_cleanup_drains_batches_independently_of_disabled_or_failing_email(bool deliveryFails)
    {
        var account = await Account();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(new MailSender { Fail = true }); }));
        if (deliveryFails)
        {
            await host.Services.GetRequiredService<IAccountEmailQueue>().EnqueueAsync(account.Id, account.Email, "Failure", "Synthetic failed delivery");
            await host.Services.GetRequiredService<AccountEmailDispatcher>().DispatchOnceAsync();
            Assert.Equal(1, await Db(db => db.AccountEmails.Where(m => m.UserId == account.Id).Select(m => m.Attempts).SingleAsync()));
        }
        await Db(async db =>
        {
            for (var i = 0; i < 253; i++)
            {
                var expiry = i < 251 ? clock.Now : clock.Now.AddSeconds(i == 251 ? 30 : 150);
                db.PortalSessions.Add(new() { Id = Guid.NewGuid().ToString("N") + i.ToString("X32"), UserId = account.Id, ExpiresUtc = expiry });
                db.AuthenticatorConsumptions.Add(new() { UserId = account.Id, KeyFingerprint = new string('A', 64), TimeStep = i, ExpiresUtc = expiry });
            }
            return await db.SaveChangesAsync();
        });
        var cleanup = new PortalSecurityCleanup(host.Services.GetRequiredService<IServiceScopeFactory>(), clock);
        using var worker = new PortalSecurityCleanupWorker(cleanup, clock, NullLogger<PortalSecurityCleanupWorker>.Instance,
            host.Services.GetRequiredService<PortalWorkerHealth>());
        await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (await Db(db => db.AuthenticatorConsumptions.CountAsync(c => c.UserId == account.Id)) != 2)
                await Task.Delay(25, timeout.Token);
            Assert.Equal(2, await Db(db => db.PortalSessions.CountAsync(s => s.UserId == account.Id)));
            Assert.NotNull(host.Services.GetRequiredService<PortalSecurityCleanup>());
        }
        finally { await worker.StopAsync(default); }
        clock.Now = clock.Now.AddSeconds(30);
        await Task.WhenAll(cleanup.CleanupAsync(), new PortalSecurityCleanup(host.Services.GetRequiredService<IServiceScopeFactory>(), clock).CleanupAsync());
        Assert.Equal(1, await Db(db => db.PortalSessions.CountAsync(s => s.UserId == account.Id)));
        Assert.Equal(252, await Db(db => db.AuthenticatorConsumptions.Where(c => c.UserId == account.Id).Select(c => c.TimeStep).SingleAsync()));
    }

    private sealed class ReadTimeouts(int failures) : DbCommandInterceptor
    {
        public int Calls { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            Calls++; if (Calls <= failures) throw new TimeoutException("Synthetic transient read.");
            return ValueTask.FromResult(result);
        }
    }
    private async Task<ApplicationDbContext> Context(params IInterceptor[] interceptors)
    {
        var connection = await Db(db => Task.FromResult(db.Database.GetConnectionString()!));
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).AddInterceptors(interceptors).Options);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, false)]
    public async Task Standalone_reads_retry_at_most_three_times(int failures, bool recovers)
    {
        var faults = new ReadTimeouts(failures); await using var db = await Context(faults);
        if (recovers) Assert.True(await SqlReadRetry.RunAsync(db, () => db.Users.AnyAsync()));
        else await Assert.ThrowsAsync<RetryLimitExceededException>(() => SqlReadRetry.RunAsync(db, () => db.Users.AnyAsync()));
        Assert.Equal(recovers ? 3 : 4, faults.Calls);
    }

    private sealed class UncertainCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        private readonly HashSet<Guid> transactions = [];
        public Guid? Target { get; set; }
        public int Commits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken token = default)
        { if (!Armed || eventData.TransactionId != Target || !transactions.Add(eventData.TransactionId)) return Task.CompletedTask; Commits++; throw new TimeoutException("The server committed but acknowledgement was lost."); }
    }

    [Fact]
    public async Task Business_writes_are_never_replayed_after_an_uncertain_commit()
    {
        var account = await Account();
        var fault = new UncertainCommit();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(fault))));
        var calls = 0; var title = "Uncertain-" + Guid.NewGuid().ToString("N");
        using var scope = host.Services.CreateScope();
        fault.Armed = true;
        var result = await scope.ServiceProvider.GetRequiredService<AdminWrites>().RunAsync(async db =>
        {
            fault.Target = db.Database.CurrentTransaction!.TransactionId;
            calls++; db.PublicAdvisories.Add(new PublicAdvisory { Title = title, Slug = title, PublishedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await AdminAudit.AddAsync(db, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id)])),
                "Create", "PublicAdvisories", title, fields: ["Title"]);
            return WriteResult.Success;
        }, default);
        Assert.False(result.Succeeded); Assert.Contains("could not be confirmed", result.Error); Assert.Equal(1, calls); Assert.Equal(1, fault.Commits);
        Assert.Equal(1, await Db(db => db.PublicAdvisories.CountAsync(a => a.Title == title)));
        Assert.Equal(1, await Db(db => db.AdminAuditLogs.CountAsync(a => a.RecordKey == title)));
    }

    private sealed class ImportCommitTarget(UncertainCommit fault, string name) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Doctor>().Any(e => e.Entity.FullName == name))
            { fault.Target = eventData.Context.Database.CurrentTransaction!.TransactionId; fault.Armed = true; }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task An_uncertain_import_commit_preserves_success_and_never_replays_business_rows_or_audits()
    {
        var isolated = new PortalFactory(); await isolated.InitializeAsync();
        try
        {
            await isolated.Services.GetServices<IHostedService>().OfType<ImportWorker>().Single().StopAsync(default);
            var name = "Uncertain import " + Guid.NewGuid().ToString("N"); var fault = new UncertainCommit();
            using var host = isolated.WithWebHostBuilder(b =>
            {
                b.UseSetting("Imports:StagingPath", Path.Combine(Path.GetDirectoryName(isolated.Services.GetRequiredService<IConfiguration>()["DataProtection:KeyRingPath"])!, "imports"));
                b.ConfigureTestServices(s =>
                {
                    s.Remove(s.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ImportWorker)));
                    s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(fault, new ImportCommitTarget(fault, name)));
                });
            });
            var email = $"import.{Guid.NewGuid():N}@fixtures.test";
            await StaffAccounts.EnsureAccountAsync(host.Services, email, true, "Admin");
            using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var actor = await db.Users.SingleAsync(u => u.Email == email); var template = ImportTemplates.Get("Doctors");
            var row = ImportExamples.Rows(template, false)[0]; row[Array.IndexOf(template.Columns, "FullName")] = name;
            var bytes = Encoding.UTF8.GetBytes(string.Join(",", template.Columns) + "\n" + string.Join(",", row) + "\n");
            var envelope = new ImportEnvelope { Content = bytes, Format = "csv", Template = template.Sheet }; var id = Guid.NewGuid();
            var preview = await ImportValidation.BuildAsync(db, envelope, default);
            Assert.True(preview.Report.Valid, System.Text.Json.JsonSerializer.Serialize(preview.Report));
            db.ChangeTracker.Clear();
            await host.Services.GetRequiredService<ImportStaging>().SaveAsync(id, envelope, default);
            var hash = ImportStaging.ApprovalHash(envelope);
            db.ImportBatches.Add(new() { Id = id, ActorId = actor.Id, ActorEmail = email, ApprovedById = actor.Id, ApprovedByEmail = email,
                ApprovedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, Template = template.Sheet, FileHash = ImportStaging.FileHash(bytes),
                ValidationHash = hash, ApprovalHash = hash, ValidationVersion = ImportTemplates.ValidationVersion, RowCount = 1, Status = ImportStatus.Queued });
            await db.SaveChangesAsync(); var workflow = scope.ServiceProvider.GetRequiredService<ImportWorkflow>();
            await workflow.ExecuteAsync(id, default); await workflow.ExecuteAsync(id, default);
            var finished = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id);
            Assert.True(fault.Commits == 1, $"Commit fault was not reached. Status: {finished.Status}; failure: {finished.FailureCode}.");
            var imported = await db.Doctors.AsNoTracking().SingleAsync(d => d.FullName == name);
            Assert.Equal(1, await db.AdminAuditLogs.CountAsync(a => a.Entity == "Doctors" && a.RecordKey == imported.Id.ToString() && a.Action == "ImportCreate"));
            Assert.Equal(ImportStatus.Succeeded, finished.Status); Assert.Equal(1, finished.CreatedCount); Assert.Null(finished.FailureCode);
        }
        finally { await ((IAsyncLifetime)isolated).DisposeAsync(); }
    }

    [Fact]
    public async Task Deleted_accounts_clear_invitation_references_and_keep_acceptance_history()
    {
        var account = await Account(); var accepted = DateTime.UtcNow.AddHours(-1);
        var id = await Db(async db =>
        {
            var invitation = new StaffInvitation { Email = account.Email, NormalizedEmail = account.Email.ToUpperInvariant(), Role = "LabStaff",
                CodeHash = RandomNumberGenerator.GetBytes(32), AcceptedByUserId = account.Id, AcceptedAtUtc = accepted,
                CreatedAtUtc = accepted.AddDays(-1), ExpiresAtUtc = accepted.AddDays(1) };
            db.StaffInvitations.Add(invitation); await db.SaveChangesAsync(); return invitation.Id;
        });
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); Assert.True((await users.DeleteAsync((await users.FindByIdAsync(account.Id))!)).Succeeded);
        }
        var retained = await Db(db => db.StaffInvitations.AsNoTracking().SingleAsync(i => i.Id == id));
        Assert.Null(retained.AcceptedByUserId); Assert.Equal(accepted, retained.AcceptedAtUtc);
    }

    private sealed class AcknowledgementLost : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<AccountEmail>().Any() && Failures == 0)
            { Failures++; throw new TimeoutException("Queued insert committed before acknowledgement was lost."); }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task An_uncertain_queue_insert_is_recovered_by_its_stable_id_without_duplicate_messages()
    {
        var account = await Account(); var fault = new AcknowledgementLost();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(fault))));
        var queue = host.Services.GetRequiredService<IAccountEmailQueue>(); fault.Armed = true;
        await queue.EnqueueAsync(account.Id, account.Email, "Reset", "IDEMPOTENT-MESSAGE");
        Assert.Equal(1, fault.Failures); Assert.Equal(1, await Db(db => db.AccountEmails.CountAsync(m => m.UserId == account.Id)));
    }

    [Fact]
    public async Task Pdf_password_checks_preserve_failed_signin_counters_and_have_an_independent_throttle()
    {
        var account = await Account();
        var labId = await Db(async db =>
        {
            var patient = new PatientRecord { FirstName = "PDF counter", LastName = "fixture", PortalUserId = account.Id };
            db.PatientRecords.Add(patient);
            var lab = new LabResult { Patient = patient, AccessionNumber = Guid.NewGuid().ToString("N"), TestName = "Counter fixture",
                Status = "Available", CollectedAt = DateTime.Today.AddDays(-2), ReleasedAt = DateTime.Today.AddDays(-1) };
            db.LabResults.Add(lab); await db.SaveChangesAsync(); return lab.Id;
        });
        using (var scope = factory.Services.CreateScope())
        {
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic PDF counter test\n%%EOF");
            var storage = scope.ServiceProvider.GetRequiredService<ILabReportStorage>();
            var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Report", "test.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            var stored = await storage.StoreAsync(labId, file, default);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var lab = await db.LabResults.FindAsync(labId);
            lab!.ReportFileName = stored.FileName; await db.SaveChangesAsync();
        }
        using var host = factory.WithWebHostBuilder(b => { b.UseSetting("PatientResults:ShowFullResults", "true");
            b.ConfigureTestServices(s => s.Configure<PortalRateLimitOptions>(o => o.PdfPerUser = 2)); });
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await client.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        await Db(async db => await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.AccessFailedCount, 2)));
        var path = $"/Patient/LabResults/Details/{labId}"; var action = $"/Patient/LabResults/Report/{labId}";
        var wrong = await client.PostFormAsync(path, action, new Dictionary<string, string> { ["password"] = "wrong", ["intent"] = "view" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode); Assert.Equal(2, await Db(db => db.Users.Where(u => u.Id == account.Id).Select(u => u.AccessFailedCount).SingleAsync()));
        var correct = await client.PostFormAsync(path, action, new Dictionary<string, string> { ["password"] = PortalFactory.Password, ["intent"] = "view" });
        Assert.Equal("application/pdf", correct.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, await Db(db => db.Users.Where(u => u.Id == account.Id).Select(u => u.AccessFailedCount).SingleAsync()));
        var rejected = await client.PostFormAsync(path, action, new Dictionary<string, string> { ["password"] = PortalFactory.Password, ["intent"] = "download" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode); Assert.NotNull(rejected.Headers.RetryAfter);
    }

    private sealed class UnavailableExtraction : IIdDocumentExtractionService
    {
        public Task<IdExtractionResult> ExtractFromBytesAsync(string idType, byte[] frontImageBytes, byte[]? backImageBytes = null) =>
            throw new OcrDependencyUnavailableException("Synthetic native library failure.");
        public Task<IdExtractionResult> ExtractFromStreamsAsync(string idType, Stream frontImageStream, Stream? backImageStream = null) =>
            throw new OcrDependencyUnavailableException("Synthetic native library failure.");
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task Valid_multipart_images_allow_manual_entry_while_dependency_failures_return_503(bool unavailable, HttpStatusCode expected)
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { if (unavailable) { s.RemoveAll<IIdDocumentExtractionService>(); s.AddSingleton<IIdDocumentExtractionService, UnavailableExtraction>(); } }));
        using var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var stream = typeof(OcrRuntime).Assembly.GetManifestResourceStream("DrmcPatientPortal.Services.OcrFixtures.probe.png")!;
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer);
        var page = await client.GetStringAsync("/Identity/Account/Register");
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Passport"), "idType");
        form.Add(new StringContent(StaffAccounts.Token(page)), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(buffer.ToArray()), "frontPhoto", "probe.png");
        var response = await client.PostAsync("/Identity/Account/Register?handler=ExtractId", form);
        Assert.Equal(expected, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("not a valid JPEG", body); Assert.DoesNotContain("Synthetic native", body);
        if (!unavailable) Assert.Contains("tempFrontToken", body);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public async Task Native_ocr_probes_decode_and_encrypted_uploads_roundtrip_without_inspecting_file_nuls(string extension)
    {
        using var stream = typeof(OcrRuntime).Assembly.GetManifestResourceStream($"DrmcPatientPortal.Services.OcrFixtures.probe.{extension}")!;
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer); var bytes = buffer.ToArray();
        Assert.Contains((byte)0, bytes);
        using var scope = factory.Services.CreateScope(); var storage = scope.ServiceProvider.GetRequiredService<IPatientDocumentStorage>();
        var form = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", "probe." + extension);
        var staged = await storage.StageAsync(form, "synthetic-session");
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.CommitAsync(staged.Token, "other-session", "synthetic-owner", "front"));
        var stored = await storage.CommitAsync(staged.Token, "synthetic-session", "synthetic-owner", "front");
        Assert.Equal(bytes, await storage.ReadAsync("synthetic-owner", stored.FileName, stored.StorageVersion));
        await storage.DeleteStoredAsync("synthetic-owner", stored.FileName);
        var corrupt = new FormFile(new MemoryStream(bytes[..12]), 0, 12, "image", "corrupt." + extension);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.StageAsync(corrupt, "synthetic-session"));
    }

    [Fact]
    public void Missing_ocr_language_data_is_an_actionable_dependency_failure()
    {
        var runtime = new OcrRuntime(Options.Create(new OcrOptions { TessDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }),
            new TestEnvironment(), NullLogger<OcrRuntime>.Instance);
        var error = Assert.Throws<OcrDependencyUnavailableException>(() => runtime.CreateEngine());
        Assert.Contains("Ocr:TessDataPath", error.Message); Assert.True(OcrRuntime.IsDependencyFailure(error));
    }

    [Fact]
    public async Task Patient_pages_use_fixed_size_sql_pagination_and_independent_schedules_and_allergies()
    {
        var account = await Account();
        var patientId = await Db(async db =>
        {
            var patient = new PatientRecord { FirstName = "Pagination", LastName = "Fixture", PortalUserId = account.Id };
            db.PatientRecords.Add(patient); await db.SaveChangesAsync();
            for (var i = 0; i < 31; i++)
            {
                db.ClinicalEncounters.Add(new() { PatientRecordId = patient.Id, EncounterDate = DateTime.Today, EncounterReference = $"PAGE-{patient.Id}-{i:D2}" });
                db.LabResults.Add(new() { PatientRecordId = patient.Id, CollectedAt = ClinicalClock.Today.AddDays(-i), AccessionNumber = $"PAGE-{patient.Id}-{i:D2}", TestName = "Paging lab", Status = "Available", ReleasedAt = ClinicalClock.Today, ReportFileName = Guid.NewGuid().ToString("N") });
                db.RadiologyStudies.Add(new() { PatientRecordId = patient.Id, PerformedAt = ClinicalClock.Today.AddDays(-i), AccessionNumber = $"PAGE-{patient.Id}-{i:D2}", StudyName = "Paging radiology", Status = RadiologyStatus.Final, ReleasedAt = ClinicalClock.Today, ReportFileName = Guid.NewGuid().ToString("N") });
                db.PatientIdDocuments.Add(new() { PatientUserId = account.Id, IdType = "Passport", IdNumber = $"ID{i:D2}", IsManualEntry = true });
                db.PatientAllergies.Add(new() { PatientRecordId = patient.Id, Allergen = $"Allergen{i:D2}" });
                db.Prescriptions.Add(new() { PatientRecordId = patient.Id, RxNumber = $"PAGE-{patient.Id}-{i:D2}", GenericName = $"Medication{i:D2}", PrescribedAt = DateTime.Today, ValidUntil = DateTime.Today.AddDays(30),
                    DoseSchedules = [new() { DoseTime = new TimeOnly(8, 0) }] });
            }
            await db.SaveChangesAsync(); return patient.Id;
        });
        using var enabled = factory.WithWebHostBuilder(b => b.UseSetting("PatientResults:ShowFullResults", "true"));
        using var client = enabled.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await client.SignInSuccessfullyAsync(account.Email, PortalFactory.Password);
        foreach (var path in new[] { "/Patient/Visits", "/Patient/LabResults", "/Patient/Radiology", "/Patient/Medications", "/Identity/Account/Manage/MyIds" })
        {
            var first = await client.GetStringAsync(path); Assert.Contains("31 total", first); Assert.Contains("Page 1 of 2", first);
            var parameter = path.Contains("MyIds", StringComparison.Ordinal) ? "idPage" : "page";
            var second = await client.GetStringAsync(path + "?" + parameter + "=2"); Assert.Contains("Page 2 of 2", second);
            Assert.Contains(parameter + "=1", second);
            if (path is "/Patient/LabResults" or "/Patient/Radiology")
            {
                var group = path.EndsWith("LabResults", StringComparison.Ordinal) ? "lab-date-group" : "radiology-date-group";
                Assert.Equal(25, System.Text.RegularExpressions.Regex.Matches(first, $"data-testid=\"{group}\"").Count);
                Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(second, $"data-testid=\"{group}\"").Count);
                Assert.Equal(25, System.Text.RegularExpressions.Regex.Matches(first, $"action=\"{path}/Report/\\d+\"").Count);
                Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(second, $"action=\"{path}/Report/\\d+\"").Count);
            }
            else if (!path.Contains("MyIds", StringComparison.Ordinal))
            {
                Assert.Equal(25, PortalClient.Links(first, path + "/Details/").Count);
                Assert.Equal(6, PortalClient.Links(second, path + "/Details/").Count);
            }
        }
        var medications = await client.GetStringAsync("/Patient/Medications?page=2&schedulePage=2&allergyPage=2");
        Assert.Contains("31 documented drug allergies", medications); Assert.Contains("Allergen30", medications);
        Assert.Contains("schedulePage pagination", medications); Assert.Contains("allergyPage pagination", medications);
        using var scope = enabled.Services.CreateScope();
        var controller = ActivatorUtilities.CreateInstance<PatientController>(scope.ServiceProvider);
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(account.Id);
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user!);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider } };
        var dashboard = Assert.IsType<PatientDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Home()).Model);
        Assert.Single(dashboard.Encounters); Assert.Single(dashboard.LabResults); Assert.Equal(31, dashboard.EncountersCount); Assert.Equal(31, dashboard.LabResultsCount);
        Assert.Equal(31, await Db(db => db.MedicationDoseSchedules.CountAsync(s => s.Prescription.PatientRecordId == patientId)));
    }

    [Fact]
    public async Task Public_directory_and_advisory_pages_preserve_filters_and_expose_records_after_the_first_25()
    {
        var prefix = "Paging" + Guid.NewGuid().ToString("N");
        await Db(async db =>
        {
            for (var i = 0; i < 31; i++)
            {
                db.Doctors.Add(new() { FullName = prefix + "-" + i.ToString("D2"), Department = "Internal Medicine", IsActive = true });
                db.PublicAdvisories.Add(new() { Title = prefix + "-" + i.ToString("D2"), Slug = prefix + "-" + i.ToString("D2"), PublishedAt = DateTime.UtcNow.Date });
            }
            return await db.SaveChangesAsync();
        });
        using var client = factory.CreatePortalClient();
        foreach (var path in new[] { "/Directory", "/Advisories" })
        {
            var first = await client.GetStringAsync(path + "?search=" + prefix);
            var second = await client.GetStringAsync(path + "?search=" + prefix + "&page=2");
            Assert.Contains("31 total", first); Assert.Contains("Page 2 of 2", second); Assert.Contains("search=" + prefix, second);
            Assert.Contains(prefix + (path == "/Directory" ? "-30" : "-00"), second);
            Assert.DoesNotContain(prefix + (path == "/Directory" ? "-30" : "-00"), first);
        }
    }
}

public sealed class InitialMigrationRollbackTests
{
    private sealed class InterruptedMigration : DbCommandInterceptor
    {
        private bool interrupted;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!interrupted && command.CommandText.Contains("CREATE TABLE [Doctors]", StringComparison.Ordinal))
            { interrupted = true; throw new InvalidOperationException("Synthetic interrupted baseline migration."); }
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Interrupted_initial_migration_rolls_back_and_can_be_applied_again()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(PortalFactory.ConnectionVariable))
        { InitialCatalog = "DrmcMigrationRollback_" + Guid.NewGuid().ToString("N") };
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection.ConnectionString).AddInterceptors(new InterruptedMigration()));
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.MaxLengthForKeys = 128).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.Database.MigrateAsync());
            await using var sql = new SqlConnection(connection.ConnectionString); await sql.OpenAsync();
            await using var query = sql.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'";
            Assert.Equal(0, (int)(await query.ExecuteScalarAsync())!);
            await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Contains("20261011120435_AddPrivateRedStarRegistry", await db.Database.GetAppliedMigrationsAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}

public sealed class KeyRingReliabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "drmc-keyring-tests-" + Guid.NewGuid().ToString("N"));
    private string Ring => Path.Combine(root, "keys");
    private static readonly XNamespace Ns = "http://schemas.asp.net/2015/03/dataProtection";

    private async Task<ServiceProvider> Services(KeyRingProtectionOptions options, bool required = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await KeyRingProtection.ConfigureAsync(services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Ring)).SetApplicationName("DRMC.PatientPortal"),
            Ring, options, required);
        var provider = services.BuildServiceProvider();
        try { KeyRingProtection.Verify(provider); return provider; }
        catch { await provider.DisposeAsync(); throw; }
    }

    private Dictionary<string, string> Files() => Directory.GetFiles(Ring, "key-*.xml").ToDictionary(file => Path.GetFileName(file)!, File.ReadAllText);
    private void Unchanged(Dictionary<string, string> before)
    {
        var after = Files();
        Assert.Equal(before.Count, after.Count);
        foreach (var (name, xml) in before) Assert.Equal(xml, after[name]);
    }

    [Fact]
    public async Task Certificate_keys_survive_restart_rotation_and_retain_historical_payloads()
    {
        var original = TestCertificates.Create(root, "original");
        byte[] payload;
        using (var services = await Services(original))
            payload = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("historical payload"u8.ToArray());
        var originalFiles = Files();
        Assert.All(originalFiles.Values, xml => Assert.Single(XDocument.Parse(xml).Descendants(Ns + "encryptedSecret")));
        using (var restarted = await Services(original))
            Assert.Equal("historical payload"u8.ToArray(), restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(payload));
        var rotated = TestCertificates.Create(root, "rotated");
        rotated.DecryptionCertificates = [new() { Path = original.CertificatePath!, PasswordFile = original.CertificatePasswordFile }];
        byte[] newer;
        using (var services = await Services(rotated))
        {
            services.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
            newer = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("new payload"u8.ToArray());
        }
        using (var services = await Services(rotated))
        {
            var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture");
            Assert.Equal("historical payload"u8.ToArray(), protector.Unprotect(payload));
            Assert.Equal("new payload"u8.ToArray(), protector.Unprotect(newer));
        }
        Assert.Equal(2, Files().Count);
        rotated.DecryptionCertificates = [];
        var before = Files();
        Assert.NotNull(await Record.ExceptionAsync(async () => { using var ignored = await Services(rotated); }));
        Unchanged(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plaintext_and_windows_dpapi_conversion_preserve_key_ids_and_existing_ciphertext(bool dpapi)
    {
        if (dpapi && !OperatingSystem.IsWindows()) return;
        byte[] payload;
        Guid[] ids;
        using (var old = await Services(new() { ProtectKeysWithDpapi = dpapi }, false))
        {
            payload = old.GetRequiredService<IDataProtectionProvider>().CreateProtector(KeyRingProtection.PatientDocumentPurpose).Protect("existing document"u8.ToArray());
            ids = old.GetRequiredService<IKeyManager>().GetAllKeys().Select(k => k.KeyId).ToArray();
        }
        var options = TestCertificates.Create(root, "portable");
        await Task.WhenAll(Convert(), Convert());
        async Task Convert() { using var ignored = await Services(options); }
        using var converted = await Services(options);
        Assert.Equal(ids, converted.GetRequiredService<IKeyManager>().GetAllKeys().Select(k => k.KeyId));
        Assert.Equal("existing document"u8.ToArray(), converted.GetRequiredService<IDataProtectionProvider>().CreateProtector(KeyRingProtection.PatientDocumentPurpose).Unprotect(payload));
        Assert.All(Files().Values, xml =>
        {
            Assert.Single(XDocument.Parse(xml).Descendants(Ns + "encryptedSecret"));
            Assert.Empty(XDocument.Parse(xml).Descendants("masterKey"));
            Assert.DoesNotContain("DpapiXmlDecryptor", xml);
        });
        Assert.Empty(Directory.GetFiles(Ring, "*.protected-*"));
        Assert.Empty(Directory.GetFiles(Ring, ".write-*"));
    }

    [Fact]
    public async Task Missing_or_conflicting_protection_is_rejected_before_key_creation()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Services(new()));
        Assert.Contains("outside Development", error.Message);
        var options = TestCertificates.Create(root, "conflict");
        options.ProtectKeysWithDpapi = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Services(options));
        Assert.False(Directory.Exists(Ring));
    }

    [Fact]
    public async Task Wrong_missing_or_public_only_certificate_and_plaintext_downgrade_preserve_the_ring()
    {
        var options = TestCertificates.Create(root, "good");
        using (var services = await Services(options))
            services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("payload");
        var before = Files();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Services(new(), false));
        var wrong = TestCertificates.Create(root, "wrong");
        Assert.NotNull(await Record.ExceptionAsync(async () => { using var ignored = await Services(wrong); }));
        var missing = new KeyRingProtectionOptions { CertificatePath = Path.Combine(root, "missing.pfx") };
        Assert.NotNull(await Record.ExceptionAsync(async () => { using var ignored = await Services(missing); }));
        var password = File.ReadAllText(options.CertificatePasswordFile!).Trim();
        using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, password);
        var publicOnly = Path.Combine(root, "public.pfx");
        using (var publicCertificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certificate.RawData))
            File.WriteAllBytes(publicOnly, publicCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
        Assert.NotNull(await Record.ExceptionAsync(async () => { using var ignored = await Services(new() { CertificatePath = publicOnly }); }));
        Unchanged(before);
    }

    [Fact]
    public async Task Unreadable_or_malformed_historical_keys_fail_without_replacing_or_generating_keys()
    {
        var options = TestCertificates.Create(root, "good");
        using (var services = await Services(options))
            services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("payload");
        var file = Directory.GetFiles(Ring, "key-*.xml").Single();
        var xml = XDocument.Load(file);
        xml.Descendants(XName.Get("CipherValue", "http://www.w3.org/2001/04/xmlenc#")).First().Value = "AAAA";
        xml.Save(file);
        var corrupt = Files();
        Assert.NotNull(await Record.ExceptionAsync(async () => { using var ignored = await Services(options); }));
        Unchanged(corrupt);
        File.WriteAllText(file, "<!DOCTYPE key [<!ENTITY external SYSTEM 'file:///not-read'>]><key>&external;</key>");
        var malformed = Files();
        await Assert.ThrowsAsync<System.Xml.XmlException>(() => Services(options));
        Unchanged(malformed);
    }

    [Fact]
    public async Task Unwritable_key_storage_is_rejected_even_before_the_first_key()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Ring, "A file cannot be a writable key directory.");
        await Assert.ThrowsAsync<IOException>(() => Services(TestCertificates.Create(root, "good")));
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(root);
        var temporary = Path.GetFullPath(Path.GetTempPath());
        if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test storage root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
