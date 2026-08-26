using QRCoder;

namespace DrmcPatientPortal.Services;

public class QrCodeService : IQrCodeService
{
    public string GenerateSvgQrCode(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svgCode = new SvgQRCode(qrCodeData);
        return svgCode.GetGraphic(4);
    }

    public string GenerateBase64QrCode(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngCode = new PngByteQRCode(qrCodeData);
        var bytes = pngCode.GetGraphic(6);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
