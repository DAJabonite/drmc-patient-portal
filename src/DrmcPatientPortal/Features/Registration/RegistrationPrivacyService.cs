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
    private const int MaximumNoticeVersionLength = 40;
    private const int MaximumPurposeLength = 120;

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

        var normalizedVersion = noticeVersion.Trim();
        if (normalizedVersion.Length > MaximumNoticeVersionLength)
        {
            throw new ArgumentException($"The notice version cannot exceed {MaximumNoticeVersionLength} characters.", nameof(noticeVersion));
        }

        var normalizedPurpose = string.IsNullOrWhiteSpace(optionalConsentPurpose)
            ? null
            : optionalConsentPurpose.Trim();

        if (optionalConsentGranted && normalizedPurpose is null)
        {
            throw new InvalidOperationException("Optional consent requires a specific purpose.");
        }

        if (normalizedPurpose?.Length > MaximumPurposeLength)
        {
            throw new ArgumentException($"The optional-consent purpose cannot exceed {MaximumPurposeLength} characters.", nameof(optionalConsentPurpose));
        }

        user.PrivacyNoticeAcknowledgedAtUtc = utcNow;
        user.PrivacyNoticeVersion = normalizedVersion;
        user.OptionalConsentGranted = optionalConsentGranted;
        user.OptionalConsentPurpose = optionalConsentGranted ? normalizedPurpose : null;
        user.OptionalConsentRecordedAtUtc = optionalConsentGranted ? utcNow : null;
    }
}
