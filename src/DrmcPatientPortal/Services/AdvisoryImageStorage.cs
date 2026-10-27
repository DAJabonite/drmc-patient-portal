using Microsoft.Extensions.Options;
using SkiaSharp;

namespace DrmcPatientPortal.Services;

public sealed class AdvisoryImageStorageOptions
{
    public string RootPath { get; set; } = "App_Data/AdvisoryImages";
}

public interface IAdvisoryImageStorage
{
    Task<string> StoreAsync(IFormFile file, CancellationToken token);
    Task<byte[]> ReadAsync(string fileName, CancellationToken token);
}

public sealed class AdvisoryImageStorage : IAdvisoryImageStorage
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const int MaximumRequestBytes = MaximumBytes + 1024 * 1024;
    public const string UrlPrefix = "/Advisories/Images/";
    public const string InvalidImageMessage = "Choose a valid static JPEG, PNG or WebP image up to 5 MiB, 4096 pixels per side and 16 megapixels.";
    private readonly PrivateStorageRoot root;

    public AdvisoryImageStorage(IWebHostEnvironment environment, IOptions<AdvisoryImageStorageOptions> options) =>
        root = new(environment, options.Value.RootPath);

    public static bool IsFileName(string? name) => name?.Length == 37 && name.EndsWith(".webp", StringComparison.Ordinal)
        && PrivateStorageRoot.IsGuid(name[..32]);
    public static bool IsManagedUrl(string? url) => url?.StartsWith(UrlPrefix, StringComparison.Ordinal) == true
        && IsFileName(url[UrlPrefix.Length..]);

    public async Task<string> StoreAsync(IFormFile file, CancellationToken token)
    {
        if (file.Length is <= 0 or > MaximumBytes) throw new InvalidDataException(InvalidImageMessage);
        var extensionType = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => ""
        };
        if (extensionType.Length == 0 || !string.Equals(extensionType, file.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(InvalidImageMessage);
        await using var input = file.OpenReadStream();
        using var encoded = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (encoded.Length + count > MaximumBytes) throw new InvalidDataException(InvalidImageMessage);
            encoded.Write(buffer, 0, count);
        }
        if (encoded.Length != file.Length) throw new InvalidDataException(InvalidImageMessage);
        IdImageValidation.ImageInfo dimensions;
        try { dimensions = IdImageValidation.Inspect(encoded, token); }
        catch (InvalidDataException) { throw new InvalidDataException(InvalidImageMessage); }
        if (dimensions.ContentType != extensionType) throw new InvalidDataException(InvalidImageMessage);
        encoded.Position = 0;
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException(InvalidImageMessage);
        if (codec.Info.Width != dimensions.Width || codec.Info.Height != dimensions.Height || codec.FrameCount > 1)
            throw new InvalidDataException(InvalidImageMessage);
        using var bitmap = new SKBitmap(new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException(InvalidImageMessage);
        token.ThrowIfCancellationRequested();
        using var normalized = bitmap.Encode(SKEncodedImageFormat.Webp, 90) ?? throw new InvalidDataException(InvalidImageMessage);
        if (normalized.Size > MaximumBytes) throw new InvalidDataException(InvalidImageMessage);
        var name = Guid.NewGuid().ToString("N") + ".webp";
        Directory.CreateDirectory(root.Resolve());
        await PrivateStorageIO.WriteNewAsync(root.Resolve(name), normalized.ToArray(), token);
        return UrlPrefix + name;
    }

    public Task<byte[]> ReadAsync(string fileName, CancellationToken token)
    {
        if (!IsFileName(fileName)) throw new FileNotFoundException("Invalid image reference.");
        return PrivateStorageIO.ReadAsync(root.Resolve(fileName), MaximumBytes, token);
    }
}
