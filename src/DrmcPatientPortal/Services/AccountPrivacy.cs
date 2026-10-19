using System.Diagnostics;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class AccountPrivacyOptions
{
    public int MinimumResponseMilliseconds { get; set; } = 250;
}

public sealed class AccountPrivacy(IOptions<AccountPrivacyOptions> options, TimeProvider clock, IOptions<PasswordHasherOptions> hashing)
{
    private readonly PasswordHasher<ApplicationUser> hasher = new(hashing);
    private readonly ApplicationUser syntheticUser = new();
    private readonly string syntheticHash = new PasswordHasher<ApplicationUser>(hashing).HashPassword(new(), "Synthetic-password-work-only");

    public void PerformPasswordWork(string password) => hasher.VerifyHashedPassword(syntheticUser, syntheticHash, password);

    public async Task FinishAsync(long started, CancellationToken token)
    {
        var minimum = TimeSpan.FromMilliseconds(options.Value.MinimumResponseMilliseconds);
        while (true)
        {
            var remaining = minimum - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return;
            // Timer resolution can wake a delay early; enforce the elapsed-time floor.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)), clock, token);
        }
    }
}
