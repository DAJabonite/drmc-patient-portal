using System.Security.Claims;
using System.Security.Cryptography;
using DrmcPatientPortal.Areas.Admin.Security;
using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed record SessionIdentity(string SessionId, string UserId, string Stamp);

public sealed class PortalSessionStore(IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task<bool> IsActiveAsync(SessionIdentity session, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        return await SqlReadRetry.RunAsync(db, () => (
            from s in db.PortalSessions.AsNoTracking()
            join u in db.Users.AsNoTracking() on s.UserId equals u.Id
            where s.Id == session.SessionId && s.UserId == session.UserId && s.RevokedUtc == null &&
                s.ExpiresUtc > now && u.SecurityStamp == session.Stamp
            select s.Id).AnyAsync(token), token);
    }

    public async Task<string> CreateAsync(string userId, string stamp, DateTimeOffset expires, SessionIdentity? previous, CancellationToken token)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Do not mint a session with a stale password proof after a concurrent password reset.
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp, token))
            throw new InvalidOperationException("The sign-in proof is no longer valid. Sign in again.");
        if (previous is not null)
            await db.PortalSessions.Where(s => s.Id == previous.SessionId && s.UserId == previous.UserId && s.RevokedUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, (DateTimeOffset?)now), token);
        db.PortalSessions.Add(new PortalSession { Id = id, UserId = userId, ExpiresUtc = expires });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return id;
    }

    public async Task RevokeAsync(SessionIdentity session, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        await SqlReadRetry.RunIdempotentAsync(db, async () =>
        {
            await db.PortalSessions.Where(s => s.Id == session.SessionId && s.UserId == session.UserId && s.RevokedUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedUtc, (DateTimeOffset?)now), token);
        }, token);
    }

    public async Task<bool> ExtendAsync(SessionIdentity session, DateTimeOffset expires, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        await SqlReadRetry.RunIdempotentAsync(db, async () =>
        {
            await db.PortalSessions.Where(s => s.Id == session.SessionId && s.UserId == session.UserId &&
                s.RevokedUtc == null && s.ExpiresUtc > now && s.ExpiresUtc < expires)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ExpiresUtc, expires), token);
        }, token);
        return await IsActiveAsync(session, token);
    }
}

public sealed class PortalCookieEvents(PortalSessionStore sessions, IOptions<IdentityOptions> identity, TimeProvider clock)
    : CookieAuthenticationEvents
{
    public const string SessionClaim = "drmc:session";
    private const string IncomingSession = "DRMC.IncomingSession";
    private const string SlidingRenewal = "DRMC.SlidingRenewal";

    private SessionIdentity? Read(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(SessionClaim);
        var user = principal.FindFirstValue(identity.Value.ClaimsIdentity.UserIdClaimType);
        var stamp = principal.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType);
        return id is { Length: 64 } && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(stamp)
            ? new(id, user, stamp) : null;
    }

    public override Task CheckSlidingExpiration(CookieSlidingExpirationContext context)
    {
        context.HttpContext.Items[SlidingRenewal] = context.ShouldRenew;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var session = Read(context.Principal!);
        if (session is not null) context.HttpContext.Items[IncomingSession] = session;
        if (session is null || !await sessions.IsActiveAsync(session, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        var lifetime = context.Properties.ExpiresUtc - context.Properties.IssuedUtc ?? context.Options.ExpireTimeSpan;
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null) return;
        if ((context.ShouldRenew || context.HttpContext.Items[SlidingRenewal] is true) &&
            !await sessions.ExtendAsync(session, clock.GetUtcNow().Add(lifetime), context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    public override async Task SigningIn(CookieSigningInContext context)
    {
        var principal = context.Principal ?? throw new InvalidOperationException("A sign-in principal is required.");
        var userId = principal.FindFirstValue(identity.Value.ClaimsIdentity.UserIdClaimType)!;
        var stamp = principal.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType)!;
        var previous = context.HttpContext.Items[IncomingSession] as SessionIdentity ?? Read(context.HttpContext.User);
        var id = await sessions.CreateAsync(userId, stamp, context.Properties.ExpiresUtc!.Value, previous, context.HttpContext.RequestAborted);
        foreach (var claimIdentity in principal.Identities)
            foreach (var claim in claimIdentity.FindAll(SessionClaim).ToArray()) claimIdentity.RemoveClaim(claim);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SessionClaim, id));
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        var session = context.HttpContext.Items[IncomingSession] as SessionIdentity ?? Read(context.HttpContext.User);
        if (session is not null) await sessions.RevokeAsync(session, context.HttpContext.RequestAborted);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/Admin")) return AdminAuthorizationResultHandler.DenyAsync(context.HttpContext);
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public static Task PreserveClaims(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.CurrentPrincipal is { } current && context.NewPrincipal is { } refreshed &&
            current.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId &&
            userId == refreshed.FindFirstValue(ClaimTypes.NameIdentifier) && refreshed.Identity is ClaimsIdentity target)
        {
            foreach (var type in new[] { "amr", SessionClaim })
                foreach (var claim in current.FindAll(type).Where(c => type != "amr" || c.Value == "mfa"))
                    if (!refreshed.HasClaim(claim.Type, claim.Value)) target.AddClaim(claim);
        }
        return Task.CompletedTask;
    }
}
