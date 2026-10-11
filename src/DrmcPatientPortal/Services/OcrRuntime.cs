using Microsoft.Extensions.Options;
using Tesseract;

namespace DrmcPatientPortal.Services;

public sealed class OcrOptions
{
    public string NativeLibraryPath { get; set; } = string.Empty;
    public string TessDataPath { get; set; } = string.Empty;
}

public sealed class OcrDependencyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class OcrRuntime : IHostedService
{
    private readonly string tessData;
    private readonly ILogger<OcrRuntime> logger;

    public OcrRuntime(IOptions<OcrOptions> options, IWebHostEnvironment environment, ILogger<OcrRuntime> logger)
    {
        this.logger = logger;
        var dataPath = options.Value.TessDataPath;
        if (string.IsNullOrWhiteSpace(dataPath))
            dataPath = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tessdata"))
                ? Path.Combine(AppContext.BaseDirectory, "tessdata") : Path.Combine(environment.ContentRootPath, "tessdata");
        tessData = Path.GetFullPath(Path.IsPathRooted(dataPath) ? dataPath : Path.Combine(environment.ContentRootPath, dataPath));
        if (!string.IsNullOrWhiteSpace(options.Value.NativeLibraryPath))
            TesseractEnviornment.CustomSearchPath = Path.GetFullPath(Path.IsPathRooted(options.Value.NativeLibraryPath)
                ? options.Value.NativeLibraryPath : Path.Combine(environment.ContentRootPath, options.Value.NativeLibraryPath));
    }

    public TesseractEngine CreateEngine()
    {
        try
        {
            if (!File.Exists(Path.Combine(tessData, "eng.traineddata"))) throw new FileNotFoundException("English OCR language data is missing.");
            return new TesseractEngine(tessData, "eng", EngineMode.Default);
        }
        catch (Exception error) when (error is not OcrDependencyUnavailableException)
        {
            throw new OcrDependencyUnavailableException(
                $"OCR is unavailable. Install compatible Tesseract and Leptonica libraries and configure Ocr:NativeLibraryPath and Ocr:TessDataPath. English language data is expected in '{tessData}'. See README Linux OCR setup.", error);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var engine = CreateEngine();
            foreach (var extension in new[] { "png", "jpg" })
            {
                using var stream = typeof(OcrRuntime).Assembly.GetManifestResourceStream($"DrmcPatientPortal.Services.OcrFixtures.probe.{extension}")
                    ?? throw new InvalidOperationException("The OCR startup image probe is missing.");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                using var image = Pix.LoadFromMemory(memory.ToArray());
                using var page = engine.Process(image);
                _ = page.GetText();
            }
            logger.LogInformation("OCR startup check passed for JPEG, PNG and English language data.");
            return Task.CompletedTask;
        }
        catch (OcrDependencyUnavailableException) { throw; }
        catch (Exception error) { throw new OcrDependencyUnavailableException("OCR startup image decoding failed. Check the native libraries using the README Linux OCR setup.", error); }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static bool IsDependencyFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or OcrDependencyUnavailableException ||
                current.GetType().Name == "LoadLibraryException") return true;
        return false;
    }
}
