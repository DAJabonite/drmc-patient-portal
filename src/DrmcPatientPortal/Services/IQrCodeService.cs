namespace DrmcPatientPortal.Services;

public interface IQrCodeService
{
    string GenerateSvgQrCode(string payload);
}
