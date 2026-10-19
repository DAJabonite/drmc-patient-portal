using DrmcPatientPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Tests;

public class OcrExecutionLimitTests
{
    [Fact]
    public async Task Extraction_deadline_cancels_the_worker_and_releases_its_slot()
    {
        var runner = new WaitingRunner();
        using var runtime = Runtime(runner, TimeSpan.FromMilliseconds(100));
        using var image = Probe();
        await Assert.ThrowsAsync<OcrTimeoutException>(() => runtime.ProcessAsync(image));
        Assert.True(runner.Cancelled);
        using var retry = Probe();
        await Assert.ThrowsAsync<OcrTimeoutException>(() => runtime.ProcessAsync(retry));
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_and_no_unbounded_queue_is_added()
    {
        var runner = new WaitingRunner();
        using var runtime = Runtime(runner, TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        using var first = Probe();
        var running = runtime.ProcessAsync(first, cancellationToken: cancellation.Token);
        await runner.Started.Task;
        using var second = Probe();
        await Assert.ThrowsAsync<OcrBusyException>(() => runtime.ProcessAsync(second));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(runner.Cancelled);
    }

    private static Stream Probe() => typeof(OcrRuntime).Assembly.GetManifestResourceStream("DrmcPatientPortal.Services.OcrFixtures.probe.png")!;
    private static OcrRuntime Runtime(IOcrProcessRunner runner, TimeSpan timeout) => new(
        Options.Create(new OcrOptions { MaxConcurrency = 1, MaxQueuedJobs = 0, ExtractionTimeout = timeout }),
        new Environment(), NullLogger<OcrRuntime>.Instance, runner);

    private sealed class WaitingRunner : IOcrProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public async Task<OcrWorkerResponse> RunAsync(OcrWorkerRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new("ok"); }
            finally { Cancelled = cancellationToken.IsCancellationRequested; }
        }
    }

    private sealed class Environment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DrmcPatientPortal";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
