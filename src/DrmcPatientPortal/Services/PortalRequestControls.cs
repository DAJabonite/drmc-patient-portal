using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;

namespace DrmcPatientPortal.Services;

public sealed class PortalRateLimitOptions
{
    public int AuthenticationPerIp { get; set; } = 60;
    public int AuthenticationPerAccount { get; set; } = 10;
    public int EmailPerIp { get; set; } = 20;
    public int EmailPerAccount { get; set; } = 3;
    public int ScansPerIp { get; set; } = 5;
    public int ScansPerUser { get; set; } = 5;
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
                    PortalRequestKind.Scan => perAccount ? options.ScansPerUser : options.ScansPerIp,
                    PortalRequestKind.Pdf => options.PdfPerUser,
                    _ => throw new InvalidOperationException("A rate limit category is required.")
                },
                Window = resource.Kind == PortalRequestKind.Email ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1),
                QueueLimit = 0, AutoReplenishment = true
            }));
    public RateLimitLease AcquireIp(PortalRequestKind kind, string address) => ip.AttemptAcquire((kind, address));
    public RateLimitLease AcquireAccount(PortalRequestKind kind, string identity, string address) =>
        account.AttemptAcquire((kind, Convert.ToHexString(HMACSHA256.HashData(key,
            Encoding.UTF8.GetBytes(identity.Trim().ToUpperInvariant() + "\n" + (kind is PortalRequestKind.Pdf or PortalRequestKind.Scan ? "" : address))))));
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
        var registrationPost = HttpMethods.IsPost(context.Request.Method) &&
            (context.Request.Path.Value ?? "").TrimEnd('/').Equals("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase);
        var action = context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();
        var labUpload = HttpMethods.IsPost(context.Request.Method) && action?.ControllerName == "LabResults" &&
            action.RouteValues.TryGetValue("area", out var area) && area == "Admin" && context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true;
        var sensitiveMultipart = (registrationPost && context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true) || labUpload;
        using var scanLease = kind == PortalRequestKind.Scan || sensitiveMultipart ? limiter.ScanConcurrency.AttemptAcquire() : null;
        if (scanLease is { IsAcquired: false }) { await Rejected(context, kind, scanLease); return; }
        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        using var scanUserLease = kind == PortalRequestKind.Scan && userId is not null ? limiter.AcquireAccount(PortalRequestKind.Scan, userId, address) : null;
        if (scanUserLease is { IsAcquired: false }) { await Rejected(context, kind, scanUserLease); return; }
        if (registrationPost || labUpload)
        {
            var maximumRequestBytes = registrationPost ? IdImageValidation.MaximumRequestBytes :
                LabReportStorage.MaximumBytes + (action!.ActionName == "UploadReport" ? 65536 : 1024 * 1024);
            if (context.Request.ContentLength > maximumRequestBytes)
            { await Invalid(context, kind, "The upload request exceeds its size limit.", 413); return; }
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = maximumRequestBytes;
            context.Request.Body = new LimitedUploadStream(context.Request.Body, maximumRequestBytes);
            context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions
            {
                MultipartBodyLengthLimit = registrationPost ? IdImageValidation.MaximumBytes : maximumRequestBytes,
                BufferBodyLengthLimit = maximumRequestBytes,
                MemoryBufferThreshold = checked((int)maximumRequestBytes + 1)
            }));
        }
        IFormCollection? form = null;
        try
        {
            if (context.Request.HasFormContentType) form = await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            if (registrationPost) await Invalid(context, kind, "The upload form is invalid. Use ID photos of at most 5 MB each.");
            else if (labUpload) await Invalid(context, kind, "Choose a PDF file no larger than 10 MiB.");
            else await Invalid(context, kind);
            return;
        }
        catch (BadHttpRequestException error) when (error.StatusCode is 400 or 413)
        { await Invalid(context, kind, "The upload request is invalid or too large.", error.StatusCode); return; }
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
        var directUpload = registrationPost && kind != PortalRequestKind.Scan && form?.Files.Any(f => f.Length > 0) == true;
        using var uploadIpLease = directUpload ? limiter.AcquireIp(PortalRequestKind.Scan, address) : null;
        if (uploadIpLease is { IsAcquired: false }) { await Rejected(context, kind, uploadIpLease); return; }
        using var uploadUserLease = directUpload && userId is not null ? limiter.AcquireAccount(PortalRequestKind.Scan, userId, address) : null;
        if (uploadUserLease is { IsAcquired: false }) { await Rejected(context, kind, uploadUserLease); return; }
        await next(context);
    }

    private static async Task Invalid(HttpContext context, PortalRequestKind kind,
        string message = "The form contains invalid text. Remove null characters and submit it again.", int statusCode = 400)
    {
        context.Response.StatusCode = statusCode;
        if (kind == PortalRequestKind.Scan) await context.Response.WriteAsJsonAsync(new { success = false, message });
        else await context.Response.WriteAsync(message);
    }

    // The explicit counter also enforces the limit for chunked requests and TestServer,
    // where a server max-body-size feature may not be available.
    private sealed class LimitedUploadStream(Stream inner, long maximumBytes) : Stream
    {
        private long read;
        private int Allowed(int count) => (int)Math.Min(count, maximumBytes - read + 1);
        private int Count(int count)
        {
            read += count;
            if (read > maximumBytes)
                throw new BadHttpRequestException("The upload request exceeds its size limit.", 413);
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..Allowed(buffer.Length)]));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static async Task Rejected(HttpContext context, PortalRequestKind kind, RateLimitLease lease)
    {
        if (kind != PortalRequestKind.Scan && HttpMethods.IsPost(context.Request.Method) &&
            (context.Request.Path.Value ?? "").TrimEnd('/').Equals("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase))
            await HospitalRecordLinkAudit.RejectedAsync(context.RequestServices.GetRequiredService<IAuditLogService>(), context, "rate_limited");
        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay.TotalSeconds : kind == PortalRequestKind.Email ? 900 : 60;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry)).ToString(CultureInfo.InvariantCulture);
        const string message = "Too many attempts. Wait before trying again.";
        if (kind == PortalRequestKind.Scan) await context.Response.WriteAsJsonAsync(new { success = false, message });
        else await context.Response.WriteAsync(message);
    }
}
