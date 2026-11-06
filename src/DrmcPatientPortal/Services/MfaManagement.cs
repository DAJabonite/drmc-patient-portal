using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class MfaPasswordInput
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string Password { get; set; } = "";
}

public enum MfaOperation { ShowSetup, Enable, Reset, Disable, GenerateRecoveryCodes }

public sealed class MfaManagementResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public ApplicationUser? User { get; init; }
    public string? SetupKey { get; init; }
    public string[]? RecoveryCodes { get; init; }
}

public sealed class MfaManagement(ApplicationDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, StaffInvitationActivator invitations)
{
    public async Task<MfaManagementResult> RunAsync(ClaimsPrincipal principal, string? password, MfaOperation operation,
        string? code = null, CancellationToken token = default)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Failed("Your account is unavailable. Sign in again.");
        if (string.IsNullOrEmpty(password)) return Failed("Enter your current password to continue.");

        // Keep failed password checks outside the mutation transaction so lockout attempts persist.
        var passwordResult = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            if (passwordResult.IsLockedOut) await signIn.SignOutAsync();
            return Failed(passwordResult.IsLockedOut
                ? "Too many attempts. Sign in again after the account lockout ends."
                : "Your current password could not be verified.");
        }
        var verifiedStamp = user.SecurityStamp;
        var verifiedVersion = user.ConcurrencyStamp;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await db.Entry(user).ReloadAsync(token);
            if (db.Entry(user).State == EntityState.Detached || user.SecurityStamp != verifiedStamp ||
                user.ConcurrencyStamp != verifiedVersion || await users.IsLockedOutAsync(user))
                return Failed("Your account changed. Reload this page and try again.");

            string? key = null;
            string[]? recoveryCodes = null;
            if (operation is MfaOperation.ShowSetup or MfaOperation.Enable)
            {
                if (user.TwoFactorEnabled) return Failed("Your authenticator is already enabled. Use Reset Authenticator Key to replace it.");
                if (operation == MfaOperation.ShowSetup)
                {
                    // Always replace disabled/pending keys: legacy disabled accounts may retain a previously enabled seed.
                    Require(await users.ResetAuthenticatorKeyAsync(user));
                    await ReplaceRecoveryCodesAsync(user, 0);
                    key = await users.GetAuthenticatorKeyAsync(user);
                    if (string.IsNullOrEmpty(key)) throw new MfaWriteException();
                }
                else
                {
                    key = await users.GetAuthenticatorKeyAsync(user);
                    if (string.IsNullOrEmpty(key)) return Failed("Set up your authenticator app before verifying a code.");
                    var verificationCode = (code ?? "").Replace(" ", "").Replace("-", "");
                    if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, verificationCode))
                    {
                        await transaction.CommitAsync(token);
                        return new() { Error = "Verification code is invalid. Check your authenticator app and try again.", SetupKey = key };
                    }
                    Require(await users.SetTwoFactorEnabledAsync(user, true));
                    recoveryCodes = await ReplaceRecoveryCodesAsync(user, 10);
                    key = null;
                }
            }
            else if (operation is MfaOperation.Reset or MfaOperation.Disable)
            {
                Require(await users.SetTwoFactorEnabledAsync(user, false));
                Require(await users.ResetAuthenticatorKeyAsync(user));
                await ReplaceRecoveryCodesAsync(user, 0);
            }
            else
            {
                if (!user.TwoFactorEnabled) return Failed("Set up your authenticator app before generating recovery codes.");
                recoveryCodes = await ReplaceRecoveryCodesAsync(user, 10);
            }

            Require(await users.UpdateSecurityStampAsync(user));
            await transaction.CommitAsync(token);
            return new() { Succeeded = true, User = user, SetupKey = key, RecoveryCodes = recoveryCodes };
        }
        catch (Exception error) when (error is MfaWriteException or DbUpdateException or SqlException)
        {
            // Never retry credential mutations or expose codes after an uncertain commit.
            // The failure page/layout may save unrelated audit entries through this scoped context.
            db.ChangeTracker.Clear();
            return Failed("The security change could not be confirmed. Reload this page before trying again.");
        }
    }

    public async Task<string?> ActivateInvitationAsync(ApplicationUser user, CancellationToken token)
    {
        try { return await invitations.TryActivateAsync(user, token); }
        finally
        {
            // Activation owns its transaction; discard any rolled-back role/user state before a layout audit save.
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> RefreshSignInAsync(ApplicationUser user, CancellationToken token)
    {
        // Invitation activation can update the stamp separately. Session creation uses a separate context,
        // so refresh only after both transactions finish. Never reload a newer stamp into an old proof.
        await signIn.ForgetTwoFactorClientAsync();
        if (!await db.Users.AsNoTracking().AnyAsync(account => account.Id == user.Id && account.SecurityStamp == user.SecurityStamp, token))
        {
            await signIn.SignOutAsync();
            return false;
        }
        try { await signIn.RefreshSignInAsync(user); return true; }
        catch (InvalidOperationException)
        {
            await signIn.SignOutAsync();
            return false;
        }
    }

    private async Task<string[]> ReplaceRecoveryCodesAsync(ApplicationUser user, int count) =>
        (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, count))?.ToArray() ?? throw new MfaWriteException();

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new MfaWriteException();
    }

    private static MfaManagementResult Failed(string message) => new() { Error = message };
    private sealed class MfaWriteException : Exception;
}
