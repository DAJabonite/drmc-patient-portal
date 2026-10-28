using Microsoft.Extensions.Options;
using Tesseract;

namespace DrmcPatientPortal.Services;

public sealed class OcrOptions
{
    public string NativeLibraryPath { get; set; } = string.Empty;
    public string TessDataPath { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; } = 2;
    public int MaxQueuedJobs { get; set; } = 2;
    public TimeSpan QueueWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ExtractionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool IsValid() => MaxConcurrency is >= 1 and <= 4 && MaxQueuedJobs is >= 0 and <= 8 &&
        QueueWaitTimeout > TimeSpan.Zero && QueueWaitTimeout <= TimeSpan.FromSeconds(5) &&
        ExtractionTimeout > TimeSpan.Zero && ExtractionTimeout <= TimeSpan.FromSeconds(10);
}

public sealed class OcrDependencyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class OcrBusyException() : Exception("ID scanning is busy. Wait a few seconds and try again.");
public sealed class OcrTimeoutException() : Exception("ID scanning took too long. Try a clearer photo or enter your details manually.");
public sealed record OcrJobResult(OcrWorkerResponse Response, string ContentType, byte[]? ValidatedBytes);

/// <summary>One process-wide budget for all untrusted native image work, including storage validation.</summary>
public sealed class OcrRuntime : IHostedService, IDisposable
{
    private readonly OcrOptions options;
    private readonly IOcrProcessRunner runner;
    private readonly ILogger<OcrRuntime> logger;
    private readonly SemaphoreSlim gate;
    private readonly CancellationTokenSource stopping = new();
    private readonly string tessData, nativePath;
    private int queued;
    private int disposed;
    private readonly object lifecycle = new();
    private Task? shutdown;

    public OcrRuntime(IOptions<OcrOptions> settings, IWebHostEnvironment environment, ILogger<OcrRuntime> logger,
        IOcrProcessRunner? runner = null)
    {
        options = settings.Value;
        if (!options.IsValid()) throw new ArgumentException("OCR execution limits are outside their supported bounds.", nameof(settings));
        this.runner = runner ?? new OcrProcessRunner();
        this.logger = logger;
        gate = new(options.MaxConcurrency, options.MaxConcurrency);
        var data = options.TessDataPath;
        if (string.IsNullOrWhiteSpace(data))
            data = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tessdata"))
                ? Path.Combine(AppContext.BaseDirectory, "tessdata") : Path.Combine(environment.ContentRootPath, "tessdata");
        tessData = Resolve(data, environment.ContentRootPath);
        nativePath = string.IsNullOrWhiteSpace(options.NativeLibraryPath) ? "" : Resolve(options.NativeLibraryPath, environment.ContentRootPath);
    }

    private static string Resolve(string path, string root) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    // Retain the engine factory API; production request paths use ProcessAsync instead.
    public TesseractEngine CreateEngine() => CreateEngine(tessData, nativePath);

    internal static TesseractEngine CreateEngine(string dataPath, string libraryPath)
    {
        try
        {
            if (!File.Exists(Path.Combine(dataPath, "eng.traineddata"))) throw new FileNotFoundException("English OCR language data is missing.");
            if (!string.IsNullOrEmpty(libraryPath)) TesseractEnviornment.CustomSearchPath = libraryPath;
            return new TesseractEngine(dataPath, "eng", EngineMode.Default);
        }
        catch (Exception error)
        {
            throw new OcrDependencyUnavailableException("OCR engine initialization failed. Check Ocr:NativeLibraryPath and Ocr:TessDataPath.", error);
        }
    }

    public async Task<OcrJobResult> ProcessAsync(Stream front, Stream? back = null, bool recognize = true,
        CancellationToken cancellationToken = default, int maximumBytes = IdImageValidation.MaximumBytes, string idType = "")
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        await EnterAsync(caller.Token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
            deadline.CancelAfter(options.ExtractionTimeout);
            var token = deadline.Token;
            try
            {
                var frontFile = await PrepareAsync(front, maximumBytes, token);
                var backFile = back is null ? null : await PrepareAsync(back, maximumBytes, token);
                var response = await runner.RunAsync(new(tessData, nativePath, recognize, frontFile, backFile, idType), token);
                token.ThrowIfCancellationRequested();
                if (response.Status == "invalid") throw new InvalidDataException(IdImageValidation.InvalidImageMessage);
                if (response.Status != "ok") throw new OcrDependencyUnavailableException("ID scanning is temporarily unavailable. Please try again later.");
                var bytes = recognize ? null : frontFile.Bytes;
                return new(response, frontFile.Info.ContentType, bytes);
            }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw new OcrTimeoutException(); }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EnterAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (gate.Wait(0)) return;
        var waiting = Interlocked.Increment(ref queued);
        try
        {
            if (waiting > options.MaxQueuedJobs || !await gate.WaitAsync(options.QueueWaitTimeout, token)) throw new OcrBusyException();
        }
        finally { Interlocked.Decrement(ref queued); }
    }

    private static async Task<OcrWorkerImage> PrepareAsync(Stream input, int maximumBytes, CancellationToken token)
    {
        var limit = Math.Min(maximumBytes, IdImageValidation.MaximumBytes);
        if (limit <= 0 || (input.CanSeek && (input.Length - input.Position <= 0 || input.Length - input.Position > limit)))
            throw new InvalidDataException("Each ID photo must be between 1 byte and 5 MB.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - total + 1)), token)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Each ID photo must be between 1 byte and 5 MB.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        await output.FlushAsync(token);
        var info = IdImageValidation.InspectIdPhoto(output, token);
        token.ThrowIfCancellationRequested();
        return new(output.ToArray(), info);
    }

    public bool IsReady { get; private set; }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var extension in new[] { "png", "jpg", "webp" })
            {
                using var stream = typeof(OcrRuntime).Assembly.GetManifestResourceStream($"DrmcPatientPortal.Services.OcrFixtures.probe.{extension}")
                    ?? throw new InvalidOperationException("The OCR startup image probe is missing.");
                await ProcessAsync(stream, cancellationToken: cancellationToken);
            }
            IsReady = true;
            logger.LogInformation("OCR worker startup check passed for JPEG, PNG, WebP and English language data.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new OcrDependencyUnavailableException("OCR startup failed. Check Ocr:NativeLibraryPath, Ocr:TessDataPath and the README OCR setup.", error);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            if (disposed != 0) return Task.CompletedTask;
            shutdown ??= StopCoreAsync();
            return shutdown.WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync()
    {
        await stopping.CancelAsync();
        for (var i = 0; i < options.MaxConcurrency; i++) await gate.WaitAsync();
        gate.Release(options.MaxConcurrency);
    }
    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed != 0) return;
            disposed = 1;
            stopping.Cancel();
            stopping.Dispose();
        }
    }

    public static bool IsDependencyFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or OcrDependencyUnavailableException ||
                current.GetType().Name == "LoadLibraryException") return true;
        return false;
    }
}
