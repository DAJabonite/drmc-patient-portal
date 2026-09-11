using DrmcPatientPortal.Models;

namespace DrmcPatientPortal.Features.Registration;

public interface IAccountLifecycleService
{
    void InitializePending(ApplicationUser user, string registrationReference, DateTime utcNow);
    void Transition(ApplicationUser user, AccountLifecycleStatus target, string? reason, DateTime utcNow);
}

public sealed class AccountLifecycleService : IAccountLifecycleService
{
    private static readonly IReadOnlyDictionary<AccountLifecycleStatus, ISet<AccountLifecycleStatus>> AllowedTransitions
        = new Dictionary<AccountLifecycleStatus, ISet<AccountLifecycleStatus>>
        {
            [AccountLifecycleStatus.PendingVerification] = new HashSet<AccountLifecycleStatus>
            {
                AccountLifecycleStatus.Active,
                AccountLifecycleStatus.CorrectionRequired,
                AccountLifecycleStatus.Rejected
            },
            [AccountLifecycleStatus.CorrectionRequired] = new HashSet<AccountLifecycleStatus>
            {
                AccountLifecycleStatus.PendingVerification,
                AccountLifecycleStatus.Rejected
            },
            [AccountLifecycleStatus.Active] = new HashSet<AccountLifecycleStatus>
            {
                AccountLifecycleStatus.Suspended
            },
            [AccountLifecycleStatus.Suspended] = new HashSet<AccountLifecycleStatus>
            {
                AccountLifecycleStatus.Active,
                AccountLifecycleStatus.Rejected
            },
            [AccountLifecycleStatus.Rejected] = new HashSet<AccountLifecycleStatus>()
        };

    public void InitializePending(ApplicationUser user, string registrationReference, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationReference);
        EnsureUtc(utcNow);

        if (user.RegistrationReference is not null)
        {
            throw new InvalidOperationException("The account already has a registration reference.");
        }

        user.RegistrationReference = registrationReference;
        user.AccountStatus = AccountLifecycleStatus.PendingVerification;
        user.AccountStatusChangedAtUtc = utcNow;
        user.AccountStatusReason = "Registration submitted for verification.";
    }

    public void Transition(ApplicationUser user, AccountLifecycleStatus target, string? reason, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUtc(utcNow);

        if (user.AccountStatus == target)
        {
            return;
        }

        if (!AllowedTransitions[user.AccountStatus].Contains(target))
        {
            throw new InvalidOperationException($"Transition from {user.AccountStatus} to {target} is not allowed.");
        }

        user.AccountStatus = target;
        user.AccountStatusChangedAtUtc = utcNow;
        user.AccountStatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Lifecycle timestamps must be UTC.", nameof(value));
        }
    }
}
