using System.Text.RegularExpressions;
using DrmcPatientPortal.Features.Registration;
using DrmcPatientPortal.Models;
using Xunit;

namespace DrmcPatientPortal.Tests;

public class AccountLifecycleFoundationTests
{
    [Fact]
    public void ExistingAccount_DefaultsToActive_ForBackwardCompatibility()
    {
        var user = new ApplicationUser();
        Assert.Equal(AccountLifecycleStatus.Active, user.AccountStatus);
        Assert.True(user.CanAccessProtectedPatientData);
    }

    [Fact]
    public void InitializePending_AssignsOpaqueReference_AndBlocksProtectedData()
    {
        var generator = new RegistrationReferenceGenerator();
        var lifecycle = new AccountLifecycleService();
        var user = new ApplicationUser();
        var reference = generator.Create();

        lifecycle.InitializePending(user, reference, DateTime.UtcNow);

        Assert.Equal(AccountLifecycleStatus.PendingVerification, user.AccountStatus);
        Assert.Equal(reference, user.RegistrationReference);
        Assert.False(user.CanAccessProtectedPatientData);
        Assert.Matches(new Regex("^REG-[A-Za-z0-9_-]{24}$"), reference);
    }

    [Fact]
    public void InitializePending_RejectsPredictableOrMalformedReference()
    {
        var lifecycle = new AccountLifecycleService();
        Assert.Throws<ArgumentException>(() =>
            lifecycle.InitializePending(new ApplicationUser(), "REG-000001", DateTime.UtcNow));
    }

    [Fact]
    public void RegistrationReferences_AreNotRepeated_InLargeSample()
    {
        var generator = new RegistrationReferenceGenerator();
        var references = Enumerable.Range(0, 10_000).Select(_ => generator.Create()).ToArray();
        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(AccountLifecycleStatus.Active)]
    [InlineData(AccountLifecycleStatus.CorrectionRequired)]
    [InlineData(AccountLifecycleStatus.Rejected)]
    public void PendingAccount_AllowsExpectedVerificationDecisions(AccountLifecycleStatus target)
    {
        var lifecycle = new AccountLifecycleService();
        var user = Pending(lifecycle);
        lifecycle.Transition(user, target, "Reviewed", DateTime.UtcNow);
        Assert.Equal(target, user.AccountStatus);
        Assert.Equal(target == AccountLifecycleStatus.Active, user.CanAccessProtectedPatientData);
    }

    [Fact]
    public void RejectedAccount_CannotBeActivatedDirectly()
    {
        var lifecycle = new AccountLifecycleService();
        var user = Pending(lifecycle);
        lifecycle.Transition(user, AccountLifecycleStatus.Rejected, "Identity could not be verified.", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            lifecycle.Transition(user, AccountLifecycleStatus.Active, "Override", DateTime.UtcNow));
    }

    [Fact]
    public void Lifecycle_RejectsNonUtcTimestamps()
    {
        var lifecycle = new AccountLifecycleService();
        Assert.Throws<ArgumentException>(() =>
            lifecycle.InitializePending(new ApplicationUser(), new RegistrationReferenceGenerator().Create(), DateTime.Now));
    }

    [Fact]
    public void PrivacyNotice_IsRequired_ButOptionalConsentIsNot()
    {
        var privacy = new RegistrationPrivacyService();
        var user = new ApplicationUser();
        var now = DateTime.UtcNow;
        privacy.Record(user, true, "2026-09", false, null, now);
        Assert.Equal(now, user.PrivacyNoticeAcknowledgedAtUtc);
        Assert.Equal("2026-09", user.PrivacyNoticeVersion);
        Assert.False(user.OptionalConsentGranted);
        Assert.Null(user.OptionalConsentPurpose);
        Assert.Null(user.OptionalConsentRecordedAtUtc);
    }

    [Fact]
    public void OptionalConsent_RequiresSpecificPurpose()
    {
        var privacy = new RegistrationPrivacyService();
        Assert.Throws<InvalidOperationException>(() =>
            privacy.Record(new ApplicationUser(), true, "2026-09", true, null, DateTime.UtcNow));
    }

    [Fact]
    public void OptionalConsent_IsCleared_WhenNotGranted()
    {
        var privacy = new RegistrationPrivacyService();
        var user = new ApplicationUser();
        var now = DateTime.UtcNow;
        privacy.Record(user, true, "2026-09", true, "Service improvement research", now);
        privacy.Record(user, true, "2026-09", false, "ignored", now.AddMinutes(1));
        Assert.False(user.OptionalConsentGranted);
        Assert.Null(user.OptionalConsentPurpose);
        Assert.Null(user.OptionalConsentRecordedAtUtc);
    }

    private static ApplicationUser Pending(AccountLifecycleService lifecycle)
    {
        var user = new ApplicationUser();
        lifecycle.InitializePending(user, new RegistrationReferenceGenerator().Create(), DateTime.UtcNow);
        return user;
    }
}
