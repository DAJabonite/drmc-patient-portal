using System.Buffers.Binary;
using DrmcPatientPortal.Services;
using SkiaSharp;

namespace DrmcPatientPortal.Tests;

public class IdImageValidationTests
{
    [Theory]
    [InlineData(5000, 3500)]
    [InlineData(8192, 3906)]
    [InlineData(4096, 4096)]
    public void Id_photo_profile_accepts_bounded_phone_dimensions_without_relaxing_other_images(int width, int height)
    {
        var bytes = Header(width, height);
        using var stream = new MemoryStream(bytes);
        var info = IdImageValidation.InspectIdPhoto(stream);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(0, stream.Position);
        Assert.Throws<InvalidDataException>(() => IdImageValidation.Inspect(stream));
    }

    [Theory]
    [InlineData(8193, 1)]
    [InlineData(8000, 4001)]
    [InlineData(0, 10)]
    public void Id_photo_profile_still_rejects_excessive_or_invalid_dimensions(int width, int height)
    {
        using var stream = new MemoryStream(Header(width, height));
        Assert.Throws<InvalidDataException>(() => IdImageValidation.InspectIdPhoto(stream));
    }

    [Fact]
    public void Id_photo_profile_retains_encoded_size_and_format_limits()
    {
        using var tooLarge = new MemoryStream(new byte[IdImageValidation.MaximumBytes + 1]);
        Assert.Throws<InvalidDataException>(() => IdImageValidation.InspectIdPhoto(tooLarge));
        using var invalid = new MemoryStream([1, 2, 3, 4]);
        Assert.Throws<InvalidDataException>(() => IdImageValidation.InspectIdPhoto(invalid));
    }

    private static byte[] Header(int width, int height)
    {
        using var bitmap = new SKBitmap(2, 2);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        // Inspection reads metadata without decoding; worker tests cover full decoding.
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }
}
