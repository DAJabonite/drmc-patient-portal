using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class PortalRateLimitOptions
{
    public int AuthenticationPerIp { get; set; } = 60;
    public int AuthenticationPerAccount { get; set; } = 10;
    public int EmailPerIp { get; set; } = 20;
    public int EmailPerAccount { get; set; } = 3;
    public int ScansPerIp { get; set; } = 5;
    public int ConcurrentScans { get; set; } = 2;
    public int PdfPerUser { get; set; } = 10;
}

public enum PortalRequestKind { None, Authentication, Email, Scan, Pdf }

public sealed class PortalRequestLimiter : IDisposable
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly PortalRateLimitOptions options;
    private readonly PartitionedRateLimiter<(PortalRequestKind Kind, string Key)> ip;
    private readonly PartitionedRateLimiter<(PortalRequestKind Kind, string Key)> account;
    public ConcurrencyLimiter ScanConcurrency { get; }

    public PortalRequestLimiter(IOptions<PortalRateLimitOptions> settings)
    {
        options = settings.Value;
        ip = Create(false);
        account = Create(true);
        ScanConcurrency = new(new ConcurrencyLimiterOptions { PermitLimit = options.ConcurrentScans, QueueLimit = 0 });
    }
    private PartitionedRateLimiter<(PortalRequestKind Kind, string Key)> Create(bool perAccount) =>
        PartitionedRateLimiter.Create<(PortalRequestKind Kind, string Key), string>(resource =>
            RateLimitPartition.GetFixedWindowLimiter($"{resource.Kind}:{resource.Key}", _ => new()
            {
                PermitLimit = resource.Kind switch
                {
                    PortalRequestKind.Authentication => perAccount ? options.AuthenticationPerAccount : options.AuthenticationPerIp,
                    PortalRequestKind.Email => perAccount ? options.EmailPerAccount : options.EmailPerIp,
                    PortalRequestKind.Scan => options.ScansPerIp,
                    PortalRequestKind.Pdf => options.PdfPerUser,
                    _ => throw new InvalidOperationException("A rate limit category is required.")
                },
                Window = resource.Kind == PortalRequestKind.Email ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1),
                QueueLimit = 0, AutoReplenishment = true
            }));
    public RateLimitLease AcquireIp(PortalRequestKind kind, string address) => ip.AttemptAcquire((kind, address));
    public RateLimitLease AcquireAccount(PortalRequestKind kind, string identity, string address) =>
        account.AttemptAcquire((kind, Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes(identity.Trim().ToUpperInvariant() + "\n" + (kind == PortalRequestKind.Pdf ? "" : address))))));
    public void Dispose() { ip.Dispose(); account.Dispose(); ScanConcurrency.Dispose(); }
}

public sealed class PortalRequestControls(RequestDelegate next)
{
    public static PortalRequestKind Kind(HttpRequest request)
    {
        var path = (request.Path.Value ?? "").TrimEnd('/');
        if (HttpMethods.IsPost(request.Method) && path.Equals("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase) &&
            request.Query["handler"].ToString().Equals("ExtractId", StringComparison.OrdinalIgnoreCase)) return PortalRequestKind.Scan;
        if (HttpMethods.IsGet(request.Method) && (path.Equals("/Identity/Account/ConfirmEmail", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/ConfirmEmailChange", StringComparison.OrdinalIgnoreCase))) return PortalRequestKind.Authentication;
        if (!HttpMethods.IsPost(request.Method)) return PortalRequestKind.None;
        if (path.StartsWith("/Patient/LabResults/Report/", StringComparison.OrdinalIgnoreCase)) return PortalRequestKind.Pdf;
        if (path.Equals("/Identity/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/ResendEmailConfirmation", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/Manage/Email", StringComparison.OrdinalIgnoreCase)) return PortalRequestKind.Email;
        if (path.Equals("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/LoginWith2fa", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/LoginWithRecoveryCode", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/ResetPassword", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/Manage/EnableAuthenticator", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/Manage/ChangePassword", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Identity/Account/Manage/SetPassword", StringComparison.OrdinalIgnoreCase)) return PortalRequestKind.Authentication;
        return PortalRequestKind.None;
    }

    public async Task InvokeAsync(HttpContext context, PortalRequestLimiter limiter)
    {
        var kind = Kind(context.Request);
        var address = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
        using var ipLease = kind is PortalRequestKind.None or PortalRequestKind.Pdf ? null : limiter.AcquireIp(kind, address);
        if (ipLease is { IsAcquired: false }) { await Rejected(context, kind, ipLease); return; }
        IFormCollection? form = null;
        try
        {
            if (context.Request.HasFormContentType) form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException) { await Invalid(context, kind); return; }
        if (context.Request.Query.Any(pair => pair.Value.Any(value => value?.Contains('\0') == true)) ||
            form?.Any(pair => pair.Value.Any(value => value?.Contains('\0') == true)) == true)
        {
            await Invalid(context, kind);
            return;
        }
        string accountKey = form?["Input.Email"].ToString() ?? "";
        if (string.IsNullOrEmpty(accountKey)) accountKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Request.Query["userId"].ToString();
        if (string.IsNullOrEmpty(accountKey) && kind == PortalRequestKind.Authentication)
        {
            var pending = await context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
            accountKey = pending.Principal?.FindFirstValue(ClaimTypes.Name) ?? "";
        }
        if (string.IsNullOrEmpty(accountKey)) accountKey = form?["Input.NewEmail"].ToString() ?? "anonymous";
        using var accountLease = kind is PortalRequestKind.Authentication or PortalRequestKind.Email or PortalRequestKind.Pdf
            ? limiter.AcquireAccount(kind, accountKey, address) : null;
        if (accountLease is { IsAcquired: false }) { await Rejected(context, kind, accountLease); return; }
        using var scanLease = kind == PortalRequestKind.Scan ? limiter.ScanConcurrency.AttemptAcquire() : null;
        if (scanLease is { IsAcquired: false }) { await Rejected(context, kind, scanLease); return; }
        await next(context);
    }

    private static async Task Invalid(HttpContext context, PortalRequestKind kind)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        const string message = "The form contains invalid text. Remove null characters and submit it again.";
        if (kind == PortalRequestKind.Scan) await context.Response.WriteAsJsonAsync(new { success = false, message });
        else await context.Response.WriteAsync(message);
    }
    private static async Task Rejected(HttpContext context, PortalRequestKind kind, RateLimitLease lease)
    {
        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay.TotalSeconds : kind == PortalRequestKind.Email ? 900 : 60;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry)).ToString(CultureInfo.InvariantCulture);
        const string message = "Too many attempts. Wait before trying again.";
        if (kind == PortalRequestKind.Scan) await context.Response.WriteAsJsonAsync(new { success = false, message });
        else await context.Response.WriteAsync(message);
    }
}
