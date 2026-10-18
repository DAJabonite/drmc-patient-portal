using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tesseract;
using SkiaSharp;

namespace DrmcPatientPortal.Services;

public sealed record OcrWorkerImage(string Path, IdImageValidation.ImageInfo Info);
public sealed record OcrWorkerRequest(string TessDataPath, string NativeLibraryPath, bool Recognize, OcrWorkerImage Front, OcrWorkerImage? Back, string IdType = "");
public sealed record OcrWorkerResponse(string Status, string FrontText = "", float FrontConfidence = 0,
    string BackText = "", float BackConfidence = 0, IdExtractionResult? Extraction = null);

public interface IOcrProcessRunner
{
    Task<OcrWorkerResponse> RunAsync(OcrWorkerRequest request, CancellationToken cancellationToken);
}

public sealed class OcrProcessRunner : IOcrProcessRunner
{
    private static ProcessStartInfo WorkerStartInfo()
    {
        var assembly = typeof(OcrWorker).Assembly.Location;
        var appHost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
        var info = new ProcessStartInfo
        {
            FileName = File.Exists(appHost) ? appHost : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(assembly)!
        };
        if (!File.Exists(appHost)) info.ArgumentList.Add(assembly);
        info.ArgumentList.Add("--ocr-worker");
        info.Environment["OMP_THREAD_LIMIT"] = "1";
        return info;
    }

    public Task<OcrWorkerResponse> RunAsync(OcrWorkerRequest request, CancellationToken cancellationToken) =>
        RunProcessAsync(WorkerStartInfo(), request, cancellationToken);

    private static async Task<OcrWorkerResponse> RunProcessAsync(ProcessStartInfo info, OcrWorkerRequest request, CancellationToken token)
    {
        using var process = new Process { StartInfo = info };
        Task<string>? output = null, errors = null;
        var started = false;
        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
            started = true;
            using var io = CancellationTokenSource.CreateLinkedTokenSource(token);
            output = ReadBoundedAsync(process.StandardOutput, 256 * 1024, io.Token);
            errors = ReadBoundedAsync(process.StandardError, 64 * 1024, io.Token);
            void CancelIo(Task _) { try { io.Cancel(); } catch (ObjectDisposedException) { } }
            _ = output.ContinueWith(CancelIo, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _ = errors.ContinueWith(CancelIo, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), io.Token);
                await process.StandardInput.FlushAsync(io.Token);
                // Keep stdin open: the worker monitors EOF to detect a dead parent.
                await process.WaitForExitAsync(io.Token);
                var json = await output;
                _ = await errors;
                if (process.ExitCode != 0) throw new OcrDependencyUnavailableException("The OCR worker exited unexpectedly.");
                var response = JsonSerializer.Deserialize<OcrWorkerResponse>(json);
                if (response is null || response.Status is not ("ok" or "invalid" or "dependency") ||
                    !float.IsFinite(response.FrontConfidence) || !float.IsFinite(response.BackConfidence))
                    throw new OcrDependencyUnavailableException("The OCR worker returned an invalid response.");
                return response;
            }
            finally { await io.CancelAsync(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OcrDependencyUnavailableException)
        { throw new OcrDependencyUnavailableException("ID scanning is temporarily unavailable. Please try again later.", error); }
        finally
        {
            if (started)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
                await process.WaitForExitAsync(CancellationToken.None);
                if (output is not null) { try { await output; } catch (Exception) { /* observed; never expose OCR output */ } }
                if (errors is not null) { try { await errors; } catch (Exception) { /* observed; never log image data */ } }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("OCR worker output exceeded its limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}

public static class OcrWorker
{
    public static async Task<int> RunAsync()
    {
        OcrWorkerResponse response;
        try
        {
            var buffer = new char[16 * 1024];
            var count = 0;
            while (true)
            {
                if (count == buffer.Length) throw new InvalidDataException();
                var read = await Console.In.ReadAsync(buffer.AsMemory(count, 1));
                if (read == 0) throw new InvalidDataException();
                if (buffer[count] == '\n') break;
                count++;
            }
            var request = JsonSerializer.Deserialize<OcrWorkerRequest>(buffer.AsSpan(0, count)) ?? throw new InvalidDataException();
            new Thread(MonitorParent) { IsBackground = true, Name = "OCR parent monitor" }.Start();
            response = Execute(request);
        }
        catch (Exception error) when (OcrRuntime.IsDependencyFailure(error)) { response = new("dependency"); }
        catch (Exception) { response = new("invalid"); }
        await Console.Out.WriteAsync(JsonSerializer.Serialize(response));
        return 0;
    }

    private static void MonitorParent()
    {
        try { _ = Console.In.Read(); }
        finally { Environment.Exit(1); }
    }

    private static OcrWorkerResponse Execute(OcrWorkerRequest request)
    {
        TesseractEngine? engine = null;
        try
        {
            if (request.Recognize)
                engine = OcrRuntime.CreateEngine(request.TessDataPath, request.NativeLibraryPath);
            else if (!string.IsNullOrEmpty(request.NativeLibraryPath)) TesseractEnviornment.CustomSearchPath = request.NativeLibraryPath;
            (string Text, float Confidence) ReadImage(OcrWorkerImage file)
            {
                using var stream = File.OpenRead(file.Path);
                if (IdImageValidation.Inspect(stream) != file.Info) throw new InvalidDataException();
                using var image = LoadPixels(file);
                if (image.Width != file.Info.Width || image.Height != file.Info.Height) throw new InvalidDataException();
                if (engine is null) return ("", 0);
                using var page = engine.Process(image);
                return (page.GetText() ?? "", page.GetMeanConfidence());
            }
            var front = ReadImage(request.Front);
            var back = request.Back is null ? (Text: "", Confidence: 0f) : ReadImage(request.Back);
            IdExtractionResult? extraction = null;
            if (request.Recognize)
            {
                extraction = new()
                {
                    IdType = request.IdType, Success = !string.IsNullOrWhiteSpace(front.Text),
                    RawTextFront = front.Text, RawTextBack = back.Text,
                    MeanConfidence = request.Back is null ? front.Confidence : (front.Confidence + back.Confidence) / 2
                };
                // Parsing also stays inside the execution deadline, including regex work.
                TesseractIdDocumentExtractionService.ParseStructuredFields(extraction, request.IdType, front.Text, back.Text);
            }
            return new("ok", front.Text, front.Confidence, back.Text, back.Confidence, extraction);
        }
        finally { engine?.Dispose(); }
    }

    private static Pix LoadPixels(OcrWorkerImage file)
    {
        if (file.Info.ContentType != "image/webp") return Pix.LoadFromMemory(File.ReadAllBytes(file.Path));
        // The bundled Windows Leptonica lacks WebP. Convert only in the killable worker.
        using var codec = SKCodec.Create(file.Path) ?? throw new InvalidDataException();
        if (codec.Info.Width != file.Info.Width || codec.Info.Height != file.Info.Height || codec.FrameCount > 1)
            throw new InvalidDataException();
        using var bitmap = new SKBitmap(new SKImageInfo(file.Info.Width, file.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException();
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException();
        return Pix.LoadFromMemory(png.ToArray());
    }
}
