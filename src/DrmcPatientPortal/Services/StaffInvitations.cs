using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Services;

public sealed class StaffInvitationOptions
{
    public const string Section = "StaffInvitations";
    public int LifetimeDays { get; set; } = 3;
    public TimeSpan Lifetime => TimeSpan.FromDays(Math.Clamp(LifetimeDays, 1, 14));
}

// Invitation codes use the hospital record code format (15 Crockford base32 characters, 75 random
// bits) but live in their own table and travel in their own URL fragment key.
public static class StaffInvitationCodes
{
    public const string FragmentKey = "invite";
    public static string SignupUrl(string registerPageUrl, string formattedCode) => $"{registerPageUrl}#{FragmentKey}={formattedCode}";
    public static string Label(StaffInvitationStatus status) => status switch
    {
        StaffInvitationStatus.Invited => "Invited",
        StaffInvitationStatus.AwaitingSetup => "Awaiting setup",
        StaffInvitationStatus.Activated => "Activated",
        StaffInvitationStatus.Revoked => "Revoked",
        _ => "Expired",
    };
}

// The very first Admin cannot get a hospital record code or an invitation, so while
// AdminBootstrap is enabled and no Admin exists yet, the configured bootstrap email may sign up
// without a code. Bootstrap still only promotes that account after email confirmation and 2FA.
public sealed class FirstAdminSignup(IConfiguration configuration, ApplicationDbContext db, ILookupNormalizer normalizer)
{
    public async Task<bool> IsOpenAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("AdminBootstrap:Enabled") || string.IsNullOrWhiteSpace(configuration["AdminBootstrap:Email"])) return false;
        return !await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                       where r.Name == StaffRoles.Admin select ur.UserId).AnyAsync(cancellationToken);
    }

    public async Task<bool> AllowsAsync(string? email, CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(email) &&
        normalizer.NormalizeEmail(email.Trim()) == normalizer.NormalizeEmail(configuration["AdminBootstrap:Email"]?.Trim()) &&
        await IsOpenAsync(cancellationToken);
}

// Grants the invited staff role once the account that accepted the invitation has a confirmed
// email and two-factor authentication. Called after email confirmation and after 2FA is enabled.
public sealed class StaffInvitationActivator(ApplicationDbContext db, UserManager<ApplicationUser> users, ILogger<StaffInvitationActivator> logger)
{
    public async Task<string?> TryActivateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!user.EmailConfirmed || !user.TwoFactorEnabled || await users.IsLockedOutAsync(user)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await db.StaffInvitations.AsNoTracking().Where(i => i.AcceptedByUserId == user.Id && i.RoleGrantedAtUtc == null && i.RevokedAtUtc == null)
            .OrderByDescending(i => i.Id).FirstOrDefaultAsync(cancellationToken);
        if (invitation is null || !StaffRoles.Grantable.Contains(invitation.Role)) return null;
        var now = DateTime.UtcNow;
        // Claim the invitation first so a concurrent call or a revoke in between cannot grant twice.
        var claimed = await db.StaffInvitations.Where(i => i.Id == invitation.Id && i.RoleGrantedAtUtc == null && i.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RoleGrantedAtUtc, now), cancellationToken);
        if (claimed != 1) return null;
        if (!await users.IsInRoleAsync(user, invitation.Role))
        {
            var granted = await users.AddToRoleAsync(user, invitation.Role);
            if (!granted.Succeeded)
            {
                await db.StaffInvitations.Where(i => i.Id == invitation.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.RoleGrantedAtUtc, (DateTime?)null), cancellationToken);
                logger.LogError("Staff invitation {InvitationId} role grant failed", invitation.Id);
                return null;
            }
        }
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return null;
        // Recorded in the staff audit history under the Admin who sent the invitation.
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorId = invitation.InvitedById, ActorEmail = invitation.InvitedByEmail, TimestampUtc = now,
            Action = "Grant", Entity = "StaffAccess", RecordKey = user.Id,
            ChangedFields = System.Text.Json.JsonSerializer.Serialize(new[] { invitation.Role, "StaffInvitation" }),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Staff invitation {InvitationId} activated", invitation.Id);
        return invitation.Role;
    }
}
