using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class PortalOperationsOptions
{
    public bool MonitorWorkers { get; set; } = true;
    public int MinimumFreeDiskMb { get; set; } = 256;
    public int QueueWarningMinutes { get; set; } = 5;
}

public sealed class PortalWorkerHealth(TimeProvider clock, IConfiguration configuration) : IDisposable
{
    private readonly DateTimeOffset started = clock.GetUtcNow();
    private readonly ConcurrentDictionary<string, DateTimeOffset> successful = new();
    private readonly Meter meter = new("DRMC.PatientPortal.Operations", "1.0");
    private readonly ConcurrentDictionary<string, double> gauges = new();
    public void Success(string worker) => successful[worker] = clock.GetUtcNow();
    public void Gauge(string name, double value) => gauges[name] = value;
    public void StartMetrics() => meter.CreateObservableGauge("drmc.portal.operations", () => gauges.Select(x => new Measurement<double>(x.Value, new KeyValuePair<string, object?>("measure", x.Key))));
    public bool WorkersReady()
    {
        if (!configuration.GetValue("PortalOperations:MonitorWorkers", true)) return true;
        if (clock.GetUtcNow() - started < TimeSpan.FromMinutes(2)) return true;
        var expected = new Dictionary<string, int> { ["imports"] = 2, ["security-cleanup"] = 10, ["temporary-documents"] = 20, ["operations"] = 2 };
        if (configuration.GetValue("AccountEmail:WorkerEnabled", true)) expected["account-email"] = 2;
        if (configuration.GetValue("Notifications:Outbox:WorkerEnabled", true)) expected["outbound-notifications"] = 2;
        if (configuration.GetValue<bool>("RedStar:WorkerEnabled") && configuration.GetValue<bool>("RedStar:RemindersEnabled")) expected["private-reminders"] = 2;
        if (configuration.GetValue<bool>("PrivateRetention:Enabled")) expected["private-retention"] = 10;
        return expected.All(x => successful.TryGetValue(x.Key, out var last) && clock.GetUtcNow() - last <= TimeSpan.FromMinutes(x.Value));
    }
    public void Dispose() => meter.Dispose();
}

public sealed class PortalReadinessHealthCheck(ApplicationDbContext db, PatientDocumentStorage storage,
    IWebHostEnvironment environment, IConfiguration configuration, IOptions<LabReportStorageOptions> reports,
    IOptions<PortalOperationsOptions> options, PortalWorkerHealth workers, OcrRuntime ocr) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ocr.IsReady || !await db.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Unhealthy();
            await PatientDocumentProtectionUpgrade.VerifyReadyAsync(db, storage, cancellationToken);
            var paths = new[] { storage.Root, storage.TemporaryRoot, new PrivateStorageRoot(environment, reports.Value.RootPath),
                new PrivateStorageRoot(environment, configuration["Imports:StagingPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMC", "ImportStaging")) };
            foreach (var root in paths)
            {
                Directory.CreateDirectory(root.Resolve());
                var probe = root.Resolve(Guid.NewGuid().ToString("N"));
                try { await PrivateStorageIO.WriteNewAsync(probe, [0], cancellationToken); }
                finally { File.Delete(probe); }
                var drive = new DriveInfo(Path.GetPathRoot(root.Path)!);
                if (drive.IsReady)
                {
                    workers.Gauge("disk.free.bytes", drive.AvailableFreeSpace);
                    if (drive.AvailableFreeSpace < options.Value.MinimumFreeDiskMb * 1024L * 1024L) return HealthCheckResult.Unhealthy();
                }
            }
            return workers.WorkersReady() ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return HealthCheckResult.Unhealthy(); }
        catch (Exception) { return HealthCheckResult.Unhealthy(); }
    }
}

public sealed class PortalOperationsWorker(IServiceScopeFactory scopes, PortalWorkerHealth health,
    IOptions<PortalOperationsOptions> options, TimeProvider clock, ILogger<PortalOperationsWorker> logger) : BackgroundService
{
    private bool warned;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.StartMetrics();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var token = deadline.Token;
                var account = db.AccountEmails.Where(x => x.Status == AccountEmailStatus.Pending || x.Status == AccountEmailStatus.Sending);
                var outbound = db.OutboundNotifications.Where(x => x.Status == OutboundNotificationStatus.Pending || x.Status == OutboundNotificationStatus.Sending);
                var oldestAccount = await account.MinAsync(x => (DateTimeOffset?)x.CreatedUtc, token);
                var oldestOutbound = await outbound.MinAsync(x => (DateTimeOffset?)x.CreatedAtUtc, token);
                var oldest = new[] { oldestAccount, oldestOutbound }.Where(x => x.HasValue).Min();
                var age = oldest.HasValue ? Math.Max(0, (clock.GetUtcNow() - oldest.Value).TotalSeconds) : 0;
                var failed = await db.AccountEmails.CountAsync(x => x.Status == AccountEmailStatus.Failed, token)
                    + await db.OutboundNotifications.CountAsync(x => x.Status == OutboundNotificationStatus.DeadLetter, token);
                var imports = db.ImportBatches.Where(x => x.Status == ImportStatus.Queued || x.Status == ImportStatus.ValidationQueued || x.Status == ImportStatus.ApprovalQueued);
                var oldestImport = await imports.MinAsync(x => (DateTime?)x.CreatedAtUtc, token);
                var importAge = oldestImport.HasValue ? Math.Max(0, (clock.GetUtcNow().UtcDateTime - oldestImport.Value).TotalSeconds) : 0;
                health.Gauge("imports.pending", await imports.CountAsync(token));
                health.Gauge("imports.oldest.seconds", importAge);
                health.Gauge("imports.failed", await db.ImportBatches.CountAsync(x => x.Status == ImportStatus.Failed, token));
                var privateQueue = db.RedStarReminders.Where(x => x.Channel == RedStarReminderChannel.Email && (x.State == RedStarDeliveryState.Pending || x.State == RedStarDeliveryState.Sending));
                var oldestPrivate = await privateQueue.MinAsync(x => (DateTimeOffset?)x.CreatedUtc, token);
                var privateAge = oldestPrivate.HasValue ? Math.Max(0, (clock.GetUtcNow() - oldestPrivate.Value).TotalSeconds) : 0;
                var privateFailed = await db.RedStarReminders.CountAsync(x => x.Channel == RedStarReminderChannel.Email && x.State == RedStarDeliveryState.Failed, token);
                health.Gauge("private-email.pending", await privateQueue.CountAsync(token));
                health.Gauge("private-email.failed", privateFailed); health.Gauge("private-email.oldest.seconds", privateAge);
                health.Gauge("email.pending", await account.CountAsync(token));
                health.Gauge("outbound.pending", await outbound.CountAsync(token));
                health.Gauge("delivery.failed", failed); health.Gauge("queue.oldest.seconds", age);
                var oldestAge = Math.Max(age, Math.Max(importAge, privateAge));
                var unhealthy = oldestAge > options.Value.QueueWarningMinutes * 60 || failed + privateFailed > 0;
                if (unhealthy && !warned) logger.LogWarning("Portal queues need attention: oldest queued age {AgeSeconds}s; terminal delivery failures {FailedCount}.", oldestAge, failed + privateFailed);
                warned = unhealthy;
                health.Success("operations");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Operational monitoring unavailable ({ErrorType}).", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken);
        }
    }
}
