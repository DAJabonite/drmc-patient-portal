using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class PortalOptions
{
    public string PublicOrigin { get; set; } = string.Empty;
}

public sealed class PortalOptionsValidator(IHostEnvironment environment, IConfiguration configuration) : IValidateOptions<PortalOptions>
{
    public ValidateOptionsResult Validate(string? name, PortalOptions options)
    {
        if (environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.PublicOrigin)) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/" ||
            origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            return ValidateOptionsResult.Fail("Portal:PublicOrigin must be an HTTPS origin, for example https://portal.example.org, without a path, credentials, query or fragment.");
        var hosts = (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!environment.IsDevelopment() && (hosts.Length == 0 || hosts.Any(h => h.Contains('*')) ||
            !hosts.Contains(origin.IdnHost, StringComparer.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail("Production AllowedHosts must explicitly include the Portal:PublicOrigin hostname and must not contain wildcards.");
        return ValidateOptionsResult.Success;
    }
}

public sealed class AccountLinks(IOptions<PortalOptions> options, LinkGenerator links, IWebHostEnvironment environment)
{
    public string Page(HttpContext context, string page, object? values = null)
    {
        var path = links.GetPathByPage(context, page, handler: null, values: values)
            ?? throw new InvalidOperationException("The account link endpoint is unavailable.");
        var origin = options.Value.PublicOrigin;
        if (string.IsNullOrWhiteSpace(origin))
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Configure Portal:PublicOrigin before deployment.");
            // Development host filtering restricts this fallback to explicitly allowed local hosts.
            origin = $"{context.Request.Scheme}://{context.Request.Host}";
        }
        return new Uri(new Uri(origin.TrimEnd('/') + "/"), path).AbsoluteUri;
    }
}
