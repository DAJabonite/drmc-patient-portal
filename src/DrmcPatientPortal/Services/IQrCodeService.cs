namespace DrmcPatientPortal.Services;

public interface IQrCodeService
{
    string GenerateSvgQrCode(string payload);
    string GenerateBase64QrCode(string payload);
}
