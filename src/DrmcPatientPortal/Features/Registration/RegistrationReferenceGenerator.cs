using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace DrmcPatientPortal.Features.Registration;

public interface IRegistrationReferenceGenerator
{
    string Create();
}

public sealed class RegistrationReferenceGenerator : IRegistrationReferenceGenerator
{
    private const int RandomByteCount = 18;

    public string Create()
        => $"REG-{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(RandomByteCount))}";
}
