using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using QRCoder;
using SkiaSharp;
using Tesseract;
using Xunit.Abstractions;

namespace DrmcPatientPortal.Tests;

public class IdImageRecognitionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(500, false)]
    [InlineData(600, true)]
    [InlineData(1000, true)]
    public async Task Small_and_low_contrast_cards_with_large_QR_codes_recover_labelled_details(int width, bool lowContrast)
    {
        var bytes = Card(width, lowContrast);
        var result = await Recognize(bytes);
        Assert.Equal("ok", result.Status);
        var fields = Assert.IsType<IdExtractionResult>(result.Extraction);
        if (fields.FirstName.Value != "Maria Clara" || (width >= 600 && !fields.DateOfBirth.Found))
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal("Maria Clara", fields.FirstName.Value);
        Assert.Equal("Santos", fields.LastName.Value);
        Assert.Equal("1234-5678-9012-3456", fields.IdNumber.Value);
        // At 500px the seven-pixel label may be unreadable. A blank DOB is safer than
        // assigning an unlabelled date; any recovered date must still match the source.
        if (width >= 600) Assert.True(fields.DateOfBirth.Found);
        if (fields.DateOfBirth.Found) Assert.Equal(new DateTime(1995, 3, 8), fields.DateOfBirth.Value.Date);
        Assert.Contains("SAMPLE", fields.Address.Value);
    }

    [Fact]
    public async Task Preprocessing_recovers_fields_missing_from_the_original_single_pass()
    {
        var bytes = Card(500, false);
        using var engine = OcrRuntime.CreateEngine(DataPath, NativePath);
        using var pix = Pix.LoadFromMemory(bytes);
        using var page = engine.Process(pix);
        var original = IdExtractionTests.Parse(PhilippineIdTypes.PhilSys, page.GetText());
        var improved = await Recognize(bytes);
        Assert.True(improved.Extraction!.FoundFieldsSummary.Count > original.FoundFieldsSummary.Count);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(500, 315)]
    public async Task Blank_images_allow_manual_entry_without_fabricated_fields(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var response = await Recognize(png.ToArray());
        Assert.Equal("ok", response.Status);
        Assert.False(response.Extraction!.Success);
        Assert.Empty(response.Extraction.FoundFieldsSummary);
    }

    [Fact]
    public async Task Adjacent_columns_use_word_coordinates_to_pair_sex_and_birth_date()
    {
        using var bitmap = new SKBitmap(1000, 630);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var typeface = FixtureTypeface();
        using var font = new SKFont(typeface, 22);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        void Text(string text, float x, float y) => canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        Text("DRIVER'S LICENSE", 35, 50);
        Text("License No: D01-23-456789", 35, 110);
        Text("Last Name: SANTOS", 35, 170);
        Text("First Name: MARIA CLARA", 35, 230);
        Text("Address: 12 SAMPLE STREET", 35, 300);
        Text("Nationality", 35, 400); Text("Sex", 300, 400); Text("Date of Birth", 450, 400); Text("Weight", 800, 400);
        Text("PHL", 35, 445); Text("F", 300, 445); Text("1995-03-08", 450, 445); Text("60", 800, 445);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var result = await Recognize(png.ToArray(), PhilippineIdTypes.DriversLicense);
        Assert.Equal("ok", result.Status);
        Assert.Equal("Female", result.Extraction!.Sex.Value);
        Assert.Equal(new DateTime(1995, 3, 8), result.Extraction.DateOfBirth.Value.Date);
        Assert.Equal("D01-23-456789", result.Extraction.IdNumber.Value);
    }

    [Fact]
    public async Task Corrupt_image_bodies_are_rejected_by_the_worker()
    {
        var bytes = Card(500, false);
        using var stream = new MemoryStream(bytes);
        var info = IdImageValidation.Inspect(stream);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await new OcrProcessRunner().RunAsync(new(DataPath, NativePath, true,
            new(bytes[..32], info), null, PhilippineIdTypes.PhilSys), deadline.Token);
        Assert.Equal("invalid", response.Status);
        Assert.Null(response.Extraction);
    }

    internal static byte[] Card(int width, bool lowContrast)
    {
        using var bitmap = new SKBitmap(width, (int)(width * .63));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(247, 250, 252));
        canvas.Scale(width / 1000f);
        using var typeface = FixtureTypeface();
        using var paint = new SKPaint { Color = lowContrast ? new SKColor(105, 110, 115) : SKColors.Black, IsAntialias = true };
        using var font = new SKFont(typeface, 14);
        void Text(string text, float x, float y, float size)
        {
            font.Size = size;
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        }
        Text("REPUBLIC OF THE PHILIPPINES", 35, 45, 27);
        Text("Philippine Identification Card", 35, 80, 20);
        Text("1234-5678-9012-3456", 35, 125, 25);
        Text("Last Name", 35, 170, 14);
        Text("SANTOS", 35, 200, 30);
        Text("Given Names", 35, 250, 14);
        Text("MARIA CLARA", 35, 280, 30);
        Text("Middle Name", 35, 330, 14);
        Text("REYES", 35, 360, 30);
        Text("Date of Birth", 35, 410, 18);
        Text("1995-03-08", 35, 440, 30);
        Text("Address", 35, 490, 14);
        Text("12 SAMPLE STREET", 35, 520, 26);
        Text("SAMPLE CITY 8100", 35, 555, 26);
        using var qr = QRCodeGenerator.GenerateQrCode(new string('X', 250), QRCodeGenerator.ECCLevel.M);
        using var qrPng = new PngByteQRCode(qr);
        using var qrBitmap = SKBitmap.Decode(qrPng.GetGraphic(4));
        canvas.DrawBitmap(qrBitmap, new SKRect(575, 145, 970, 540), SKSamplingOptions.Default);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    // The no-dependencies Linux Skia build has no system-font discovery. Load the
    // same licensed font on every host so fixtures contain text rather than blank cards.
    private static SKTypeface FixtureTypeface() =>
        SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "OcrFixtures", "Arimo.ttf"))
        ?? throw new InvalidOperationException("The bundled OCR fixture font could not be loaded.");

    private static string DataPath => Environment.GetEnvironmentVariable("Ocr__TessDataPath") is { Length: > 0 } path
        ? path : Path.Combine(AppContext.BaseDirectory, "tessdata");
    private static string NativePath => Environment.GetEnvironmentVariable("Ocr__NativeLibraryPath") ?? "";

    private static async Task<OcrWorkerResponse> Recognize(byte[] bytes, string type = PhilippineIdTypes.PhilSys)
    {
        using var stream = new MemoryStream(bytes);
        var image = new OcrWorkerImage(bytes, IdImageValidation.Inspect(stream));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await new OcrProcessRunner().RunAsync(new(DataPath, NativePath, true, image, null, type), deadline.Token);
    }
}
