using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Tesseract;
using SkiaSharp;

namespace DrmcPatientPortal.Services;

public sealed record OcrWorkerImage(byte[] Bytes, IdImageValidation.ImageInfo Info);
public sealed record OcrWorkerRequest(string TessDataPath, string NativeLibraryPath, bool Recognize, OcrWorkerImage Front, OcrWorkerImage? Back, string IdType = "");
public sealed record OcrWorkerResponse(string Status, string FrontText = "", float FrontConfidence = 0,
    string BackText = "", float BackConfidence = 0, IdExtractionResult? Extraction = null);

public interface IOcrProcessRunner
{
    Task<OcrWorkerResponse> RunAsync(OcrWorkerRequest request, CancellationToken cancellationToken);
}

// Metadata is small JSON; image bodies travel as bounded binary frames, never as paths,
// command-line arguments, base64 JSON, or plaintext temporary files.
public static class OcrWorkerProtocol
{
    public const int MaximumMetadataBytes = 16 * 1024;
    private sealed record ImageHeader(int Length, IdImageValidation.ImageInfo Info);
    private sealed record Header(string TessDataPath, string NativeLibraryPath, bool Recognize,
        ImageHeader Front, ImageHeader? Back, string IdType);

    private static void ValidateLength(int length)
    {
        if (length is <= 0 or > IdImageValidation.MaximumBytes) throw new InvalidDataException("Invalid OCR image length.");
    }

    public static async Task WriteAsync(Stream output, OcrWorkerRequest request, CancellationToken token)
    {
        ValidateLength(request.Front.Bytes.Length);
        if (request.Back is { } back) ValidateLength(back.Bytes.Length);
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new Header(request.TessDataPath, request.NativeLibraryPath, request.Recognize,
            new(request.Front.Bytes.Length, request.Front.Info),
            request.Back is null ? null : new(request.Back.Bytes.Length, request.Back.Info), request.IdType));
        if (metadata.Length > MaximumMetadataBytes) throw new InvalidDataException("OCR metadata exceeds its limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, metadata.Length);
        await output.WriteAsync(prefix, token);
        await output.WriteAsync(metadata, token);
        await output.WriteAsync(request.Front.Bytes, token);
        if (request.Back is not null) await output.WriteAsync(request.Back.Bytes, token);
        await output.FlushAsync(token);
    }

    public static async Task<OcrWorkerRequest> ReadAsync(Stream input, CancellationToken token = default)
    {
        var prefix = new byte[4];
        await input.ReadExactlyAsync(prefix, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMetadataBytes) throw new InvalidDataException("Invalid OCR metadata length.");
        var metadata = new byte[length];
        await input.ReadExactlyAsync(metadata, token);
        var header = JsonSerializer.Deserialize<Header>(metadata) ?? throw new InvalidDataException();
        if (header.Front?.Info is null || (header.Back is not null && header.Back.Info is null)) throw new InvalidDataException();
        ValidateLength(header.Front.Length);
        if (header.Back is not null) ValidateLength(header.Back.Length);
        async Task<OcrWorkerImage> ReadImage(ImageHeader image)
        {
            var bytes = new byte[image.Length];
            await input.ReadExactlyAsync(bytes, token);
            return new(bytes, image.Info);
        }
        var front = await ReadImage(header.Front);
        var back = header.Back is null ? null : await ReadImage(header.Back);
        return new(header.TessDataPath, header.NativeLibraryPath, header.Recognize, front, back, header.IdType);
    }
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
                await OcrWorkerProtocol.WriteAsync(process.StandardInput.BaseStream, request, io.Token);
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
            var input = Console.OpenStandardInput();
            var request = await OcrWorkerProtocol.ReadAsync(input);
            new Thread(() => MonitorParent(input)) { IsBackground = true, Name = "OCR parent monitor" }.Start();
            response = Execute(request);
        }
        catch (Exception error) when (OcrRuntime.IsDependencyFailure(error)) { response = new("dependency"); }
        catch (Exception) { response = new("invalid"); }
        await Console.Out.WriteAsync(JsonSerializer.Serialize(response));
        return 0;
    }

    private static void MonitorParent(Stream input)
    {
        try { _ = input.ReadByte(); }
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
            var elapsed = Stopwatch.StartNew();
            var recognition = engine is null ? null : new IdImageRecognition(engine, request.IdType, elapsed);
            IdImageRecognition.Result ReadImage(OcrWorkerImage file, bool back)
            {
                using var stream = new MemoryStream(file.Bytes, writable: false);
                if (IdImageValidation.Inspect(stream) != file.Info) throw new InvalidDataException();
                using var image = LoadPixels(file);
                if (image.Width != file.Info.Width || image.Height != file.Info.Height) throw new InvalidDataException();
                if (recognition is null) return new("", 0, new());
                return recognition.Read(image, back);
            }
            var front = ReadImage(request.Front, false);
            var back = request.Back is null ? null : ReadImage(request.Back, true);
            IdExtractionResult? extraction = null;
            if (request.Recognize)
            {
                extraction = new()
                {
                    IdType = request.IdType,
                    RawTextFront = front.OriginalText, RawTextBack = back?.OriginalText ?? "",
                    MeanConfidence = back is null ? front.Confidence : (front.Confidence + back.Confidence) / 2
                };
                // Preserve per-field confidence when joining the two sides of this document.
                Copy(front.Fields);
                if (back is not null) Copy(back.Fields);
                void Copy(IdExtractionResult fields)
                {
                    if (fields.FirstName.Found) extraction.FirstName = fields.FirstName;
                    if (fields.MiddleName.Found) extraction.MiddleName = fields.MiddleName;
                    if (fields.LastName.Found) extraction.LastName = fields.LastName;
                    if (fields.IdNumber.Found) extraction.IdNumber = fields.IdNumber;
                    if (fields.DateOfBirth.Found) extraction.DateOfBirth = fields.DateOfBirth;
                    if (fields.Address.Found) extraction.Address = fields.Address;
                    if (fields.Sex.Found) extraction.Sex = fields.Sex;
                    if (fields.BloodType.Found) extraction.BloodType = fields.BloodType;
                    if (fields.FullName.Found) extraction.FullName = fields.FullName;
                    extraction.Success = extraction.FoundFieldsSummary.Count > 0;
                }
            }
            return new("ok", front.OriginalText, front.Confidence, back?.OriginalText ?? "", back?.Confidence ?? 0, extraction);
        }
        finally { engine?.Dispose(); }
    }

    private static Pix LoadPixels(OcrWorkerImage file)
    {
        if (file.Info.ContentType != "image/webp") return Pix.LoadFromMemory(file.Bytes);
        // The bundled Windows Leptonica lacks WebP. Convert only in the killable worker.
        using var source = new MemoryStream(file.Bytes, writable: false);
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException();
        if (codec.Info.Width != file.Info.Width || codec.Info.Height != file.Info.Height || codec.FrameCount > 1)
            throw new InvalidDataException();
        using var bitmap = new SKBitmap(new SKImageInfo(file.Info.Width, file.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) throw new InvalidDataException();
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException();
        return Pix.LoadFromMemory(png.ToArray());
    }
}
