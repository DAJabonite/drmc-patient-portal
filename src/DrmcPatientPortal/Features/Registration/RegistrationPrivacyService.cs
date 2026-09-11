using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Features.Registration;

public interface IRegistrationPrivacyService
{
    void Record(
        ApplicationUser user,
        bool noticeAcknowledged,
        string noticeVersion,
        bool optionalConsentGranted,
        string? optionalConsentPurpose,
        DateTime utcNow);
}

public sealed class RegistrationPrivacyService : IRegistrationPrivacyService
{
    public void Record(
        ApplicationUser user,
        bool noticeAcknowledged,
        string noticeVersion,
        bool optionalConsentGranted,
        string? optionalConsentPurpose,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(noticeVersion);

        if (!noticeAcknowledged)
        {
            throw new InvalidOperationException("The current privacy notice must be acknowledged before registration.");
        }

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Privacy timestamps must be UTC.", nameof(utcNow));
        }

        if (optionalConsentGranted && string.IsNullOrWhiteSpace(optionalConsentPurpose))
        {
            throw new InvalidOperationException("Optional consent requires a specific purpose.");
        }

        user.PrivacyNoticeAcknowledgedAtUtc = utcNow;
        user.PrivacyNoticeVersion = noticeVersion.Trim();
        user.OptionalConsentGranted = optionalConsentGranted;
        user.OptionalConsentPurpose = optionalConsentGranted ? optionalConsentPurpose!.Trim() : null;
        user.OptionalConsentRecordedAtUtc = optionalConsentGranted ? utcNow : null;
    }
}
