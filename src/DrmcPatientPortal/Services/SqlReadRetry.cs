using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using System.Security.Claims;

namespace DrmcPatientPortal.Services;

// Deliberately opt-in: retrying SaveChanges or a complete HTTP request can duplicate writes.
public static class SqlReadRetry
{
    public static Task<T> RunAsync<T>(ApplicationDbContext db, Func<Task<T>> read, CancellationToken token = default) =>
        db.Database.CurrentTransaction is not null ? read() : Strategy(db).ExecuteAsync(_ => read(), token);

    // Call only for operations with a stable key and an idempotent outcome.
    public static Task RunIdempotentAsync(ApplicationDbContext db, Func<Task> operation, CancellationToken token = default) =>
        Strategy(db).ExecuteAsync(_ => operation(), token);

#pragma warning disable EF1001 // SQL Server's own detector, rather than a broad exception retry policy.
    private static SqlServerRetryingExecutionStrategy Strategy(ApplicationDbContext db) => new ReadStrategy(db);
    private sealed class ReadStrategy(ApplicationDbContext db) : SqlServerRetryingExecutionStrategy(db, 3, TimeSpan.FromSeconds(2), null)
    {
        // The deliberately non-retrying context strategy wraps transient query errors.
        // Unwrap that failure only inside this explicitly opted-in read/idempotency boundary.
        protected override bool ShouldRetryOn(Exception error) => base.ShouldRetryOn(
            error is InvalidOperationException { InnerException: Microsoft.Data.SqlClient.SqlException or TimeoutException } wrapped
                ? wrapped.InnerException! : error);
    }
#pragma warning restore EF1001
}

public sealed class ResilientUserStore(ApplicationDbContext context, IdentityErrorDescriber describer)
    : UserStore<ApplicationUser, IdentityRole, ApplicationDbContext, string>(context, describer)
{
    public override Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.FindByIdAsync(userId, token), token);
    public override Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.FindByEmailAsync(normalizedEmail, token), token);
    public override Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.FindByNameAsync(normalizedUserName, token), token);
    public override Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.GetRolesAsync(user, token), token);
    public override Task<IList<Claim>> GetClaimsAsync(ApplicationUser user, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.GetClaimsAsync(user, token), token);
    public override Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.GetLoginsAsync(user, token), token);
    public override Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.GetAuthenticatorKeyAsync(user, token), token);
}

public sealed class ResilientRoleStore(ApplicationDbContext context, IdentityErrorDescriber describer)
    : RoleStore<IdentityRole, ApplicationDbContext, string>(context, describer)
{
    public override Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.FindByIdAsync(roleId, token), token);
    public override Task<IdentityRole?> FindByNameAsync(string normalizedName, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.FindByNameAsync(normalizedName, token), token);
    public override Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken token = default) =>
        SqlReadRetry.RunAsync(Context, () => base.GetClaimsAsync(role, token), token);
}
