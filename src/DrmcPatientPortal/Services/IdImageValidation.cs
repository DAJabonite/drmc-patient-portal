using System.Buffers.Binary;

namespace DrmcPatientPortal.Services;

/// <summary>Inspects bounded encoded data without invoking an image decoder.</summary>
public static class IdImageValidation
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const long MaximumRequestBytes = 12 * 1024 * 1024;
    public const int MaximumIdDimension = 8192;
    public const long MaximumIdPixels = 32_000_000;
    public const string InvalidImageMessage = "Upload a valid, non-animated JPEG, PNG or WebP image.";
    public sealed record ImageInfo(string ContentType, int Width, int Height);

    public static ImageInfo Inspect(Stream stream, CancellationToken cancellationToken = default) => Inspect(stream, cancellationToken, idPhoto: false);

    public static ImageInfo InspectIdPhoto(Stream stream, CancellationToken cancellationToken = default) => Inspect(stream, cancellationToken, idPhoto: true);

    private static ImageInfo Inspect(Stream stream, CancellationToken cancellationToken, bool idPhoto)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanSeek || stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Each ID photo must be between 1 byte and 5 MB.");
        stream.Position = 0;
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            var signature = reader.ReadBytes(12);
            stream.Position = 0;
            if (signature.Length >= 8 && signature.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                return Png(reader, cancellationToken, idPhoto);
            if (signature.Length >= 2 && signature[0] == 255 && signature[1] == 216) return Jpeg(reader, cancellationToken, idPhoto);
            if (signature.Length == 12 && signature.AsSpan(0, 4).SequenceEqual("RIFF"u8) && signature.AsSpan(8, 4).SequenceEqual("WEBP"u8))
                return WebP(reader, cancellationToken, idPhoto);
            throw Invalid();
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException(InvalidImageMessage, ex); }
        finally { stream.Position = 0; }
    }

    private static ImageInfo Dimensions(string type, long width, long height, bool idPhoto)
    {
        var side = idPhoto ? MaximumIdDimension : 4096;
        var pixels = idPhoto ? MaximumIdPixels : 16_000_000;
        if (width <= 0 || height <= 0 || width > side || height > side || width * height > pixels)
            throw new InvalidDataException($"Images must be at most {side} pixels wide and high, and at most {pixels / 1_000_000} megapixels.");
        return new(type, (int)width, (int)height);
    }

    private static InvalidDataException Invalid() => new(InvalidImageMessage);
    private static byte[] Read(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw Invalid();
        return bytes;
    }
    private static void Skip(BinaryReader reader, long count)
    {
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
        reader.BaseStream.Seek(count, SeekOrigin.Current);
    }
    private static uint Big32(BinaryReader reader) => BinaryPrimitives.ReadUInt32BigEndian(Read(reader, 4));
    private static int Big16(BinaryReader reader) => BinaryPrimitives.ReadUInt16BigEndian(Read(reader, 2));

    private static ImageInfo Png(BinaryReader reader, CancellationToken token, bool idPhoto)
    {
        Skip(reader, 8);
        if (Big32(reader) != 13 || !Read(reader, 4).AsSpan().SequenceEqual("IHDR"u8)) throw Invalid();
        var width = Big32(reader);
        var height = Big32(reader);
        var info = Dimensions("image/png", width, height, idPhoto);
        Skip(reader, 9); // remaining IHDR fields and CRC; full decoding validates the encoded data.
        var data = false;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            token.ThrowIfCancellationRequested();
            var length = Big32(reader);
            var type = System.Text.Encoding.ASCII.GetString(Read(reader, 4));
            if (type is "acTL" or "fcTL" or "fdAT" or "IHDR") throw Invalid();
            if (type == "IDAT") data = true;
            Skip(reader, (long)length + 4);
            if (type == "IEND")
            {
                if (length != 0 || !data || reader.BaseStream.Position != reader.BaseStream.Length) throw Invalid();
                return info;
            }
        }
        throw Invalid();
    }

    private static ImageInfo Jpeg(BinaryReader reader, CancellationToken token, bool idPhoto)
    {
        Skip(reader, 2);
        ImageInfo? info = null;
        var scan = false;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            token.ThrowIfCancellationRequested();
            var prefix = reader.ReadByte();
            if (scan && prefix != 255) continue;
            if (prefix != 255) throw Invalid();
            int marker;
            do { marker = reader.ReadByte(); } while (marker == 255);
            if (scan && (marker == 0 || marker is >= 0xd0 and <= 0xd7)) continue;
            if (marker == 0xd9)
            {
                if (info is null || !scan || reader.BaseStream.Position != reader.BaseStream.Length) throw Invalid();
                return info;
            }
            if (marker is 0 or 0xd8 or 0xdc || marker is >= 0xd0 and <= 0xd7) throw Invalid();
            var length = Big16(reader) - 2;
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
            {
                if (info is not null || marker is not (0xc0 or 0xc1 or 0xc2) || length < 6) throw Invalid();
                _ = reader.ReadByte();
                var height = Big16(reader);
                var width = Big16(reader);
                info = Dimensions("image/jpeg", width, height, idPhoto);
                Skip(reader, length - 5);
            }
            else
            {
                if (marker == 0xda && info is null) throw Invalid();
                Skip(reader, length);
            }
            if (marker == 0xda) scan = true;
        }
        throw Invalid();
    }

    private static ImageInfo WebP(BinaryReader reader, CancellationToken token, bool idPhoto)
    {
        Skip(reader, 4);
        if ((long)reader.ReadUInt32() + 8 != reader.BaseStream.Length) throw Invalid();
        Skip(reader, 4);
        ImageInfo? canvas = null, image = null;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            token.ThrowIfCancellationRequested();
            var type = System.Text.Encoding.ASCII.GetString(Read(reader, 4));
            var length = reader.ReadUInt32();
            var start = reader.BaseStream.Position;
            if ((long)length + (length & 1) > reader.BaseStream.Length - start) throw Invalid();
            if (type is "ANIM" or "ANMF") throw Invalid();
            if (type == "VP8X")
            {
                if (length != 10 || canvas is not null || image is not null) throw Invalid();
                var header = Read(reader, 10);
                if ((header[0] & 2) != 0) throw Invalid();
                canvas = Dimensions("image/webp", 1 + Little24(header.AsSpan(4)), 1 + Little24(header.AsSpan(7)), idPhoto);
            }
            else if (type == "VP8 ")
            {
                if (length < 10 || image is not null) throw Invalid();
                var header = Read(reader, 10);
                if ((header[0] & 1) != 0 || !header.AsSpan(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a })) throw Invalid();
                image = Dimensions("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)) & 0x3fff,
                    BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) & 0x3fff, idPhoto);
            }
            else if (type == "VP8L")
            {
                if (length < 5 || image is not null || reader.ReadByte() != 0x2f) throw Invalid();
                var bits = reader.ReadUInt32();
                if ((bits >> 29) != 0) throw Invalid();
                image = Dimensions("image/webp", 1 + (bits & 0x3fff), 1 + ((bits >> 14) & 0x3fff), idPhoto);
            }
            Skip(reader, start + length + (length & 1) - reader.BaseStream.Position);
        }
        if (image is null || (canvas is not null && (canvas.Width != image.Width || canvas.Height != image.Height))) throw Invalid();
        return image;
    }
    private static int Little24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
