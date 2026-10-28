using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;
using QRCoder;
using SkiaSharp;
using Tesseract;
using Xunit.Abstractions;
using System.Buffers.Binary;

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

    [Theory]
    [InlineData(1, "RGBY")]
    [InlineData(2, "GRYB")]
    [InlineData(3, "YBGR")]
    [InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")]
    [InlineData(6, "BRYG")]
    [InlineData(7, "YGBR")]
    [InlineData(8, "GYRB")]
    public void Phone_jpeg_exif_orientation_is_applied_including_mirrored_images(int orientation, string expectedCorners)
    {
        using var source = new SKBitmap(120, 80);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint())
        {
            foreach (var (color, rectangle) in new[]
            {
                (SKColors.Red, new SKRect(0, 0, 60, 40)), (SKColors.Green, new SKRect(60, 0, 120, 40)),
                (SKColors.Blue, new SKRect(0, 40, 60, 80)), (SKColors.Yellow, new SKRect(60, 40, 120, 80))
            }) { paint.Color = color; canvas.DrawRect(rectangle, paint); }
        }
        using var jpeg = source.Encode(SKEncodedImageFormat.Jpeg, 100);
        var bytes = WithExif(jpeg.ToArray(), orientation);
        using var stream = new MemoryStream(bytes);
        using var normalized = OcrWorker.DecodeImage(new(bytes, IdImageValidation.InspectIdPhoto(stream)));
        Assert.Equal(orientation >= 5 ? 80 : 120, normalized.Width);
        Assert.Equal(orientation >= 5 ? 120 : 80, normalized.Height);
        var colors = new Dictionary<char, SKColor> { ['R'] = SKColors.Red, ['G'] = SKColors.Green, ['B'] = SKColors.Blue, ['Y'] = SKColors.Yellow };
        var corners = new[] { (.25f, .25f), (.75f, .25f), (.25f, .75f), (.75f, .75f) };
        for (var i = 0; i < corners.Length; i++)
        {
            var expected = colors[expectedCorners[i]];
            var actual = normalized.GetPixel((int)(normalized.Width * corners[i].Item1), (int)(normalized.Height * corners[i].Item2));
            Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, 15);
            Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, 15);
            Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, 15);
        }
    }

    [Fact]
    public void Large_phone_photos_are_decoded_to_a_bounded_ocr_image()
    {
        using var bitmap = new SKBitmap(5000, 3500);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.White);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        var bytes = jpeg.ToArray();
        using var stream = new MemoryStream(bytes);
        using var normalized = OcrWorker.DecodeImage(new(bytes, IdImageValidation.InspectIdPhoto(stream)));
        Assert.InRange(Math.Max(normalized.Width, normalized.Height), 1, 2048);
        Assert.InRange(normalized.Width / (double)normalized.Height, 5000d / 3500 - .01, 5000d / 3500 + .01);
    }

    [Theory]
    [InlineData(3f, false)]
    [InlineData(-3f, true)]
    public async Task Lto_phone_capture_with_mild_skew_and_indoor_lighting_recovers_structured_fields(float angle, bool lowContrast)
    {
        using var bitmap = new SKBitmap(1200, 800);
        using var canvas = new SKCanvas(bitmap);
        using var background = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1200, 800),
                [new SKColor(245, 245, 235), new SKColor(190, 190, 180)], null, SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 0, 1200, 800), background);
        canvas.Translate(600, 400);
        canvas.RotateDegrees(angle);
        canvas.Skew(.02f, 0);
        canvas.Translate(-600, -400);
        using var typeface = FixtureTypeface();
        using var font = new SKFont(typeface, 26);
        using var paint = new SKPaint { Color = lowContrast ? new SKColor(90, 90, 85) : SKColors.Black, IsAntialias = true };
        void Text(string text, float y, float size = 26) { font.Size = size; canvas.DrawText(text, 95, y, SKTextAlign.Left, font, paint); }
        Text("REPUBLIC OF THE PHILIPPINES", 80);
        Text("LAND TRANSPORTATION OFFICE", 125);
        Text("DRIVER'S LICENSE", 170, 30);
        Text("Last Name, First Name, Middle Name", 245, 22);
        Text("DELA CRUZ, JUAN PEDRO GARCIA", 295, 32);
        Text("Date of Birth (YYYY/MM/DD)", 365, 22);
        Text("1995/03/08", 410, 30);
        Text("License No: D01-23-456789", 475);
        Text("Address: 12 SAMPLE STREET SAMPLE CITY", 540, 24);
        Text("Sex: M", 600);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        var result = await Recognize(jpeg.ToArray(), PhilippineIdTypes.DriversLicense);
        Assert.Equal("ok", result.Status);
        Assert.Equal("Dela Cruz", result.Extraction!.LastName.Value);
        Assert.Equal("Juan Pedro", result.Extraction.FirstName.Value);
        Assert.Equal("Garcia", result.Extraction.MiddleName.Value);
        Assert.Equal(new DateTime(1995, 3, 8), result.Extraction.DateOfBirth.Value.Date);
    }

    private static byte[] WithExif(byte[] jpeg, int orientation)
    {
        var payload = new byte[32];
        "Exif\0\0"u8.CopyTo(payload);
        var tiff = payload.AsSpan(6);
        tiff[0] = tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[4..], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[8..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[10..], 0x112);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[12..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[14..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[18..], (ushort)orientation);
        return [0xff, 0xd8, 0xff, 0xe1, 0, 34, .. payload, .. jpeg[2..]];
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

    // Keep the language model fixed as well as the pixels. Host OCR startup tests
    // separately exercise the configured distro model and native dependencies.
    private static string DataPath => Path.Combine(AppContext.BaseDirectory, "tessdata");
    private static string NativePath => Environment.GetEnvironmentVariable("Ocr__NativeLibraryPath") ?? "";

    private static async Task<OcrWorkerResponse> Recognize(byte[] bytes, string type = PhilippineIdTypes.PhilSys)
    {
        using var stream = new MemoryStream(bytes);
        var image = new OcrWorkerImage(bytes, IdImageValidation.Inspect(stream));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await new OcrProcessRunner().RunAsync(new(DataPath, NativePath, true, image, null, type), deadline.Token);
    }
}
