using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "Notifications:Email";
    public string? Provider { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;

    internal string SelectedProvider(bool development) => string.IsNullOrWhiteSpace(Provider)
        ? development ? "Console" : "Smtp"
        : Provider.Trim();

    internal string AuthenticationPassword => string.Equals(Host?.Trim(), "smtp.gmail.com", StringComparison.OrdinalIgnoreCase)
        ? string.Concat((Password ?? string.Empty).Where(c => !char.IsWhiteSpace(c)))
        : Password ?? string.Empty;
}

public sealed class EmailDeliveryOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailDeliveryOptions options)
    {
        var provider = options.SelectedProvider(environment.IsDevelopment());
        if (string.Equals(provider, "Console", StringComparison.OrdinalIgnoreCase))
            return environment.IsDevelopment() ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Console email delivery is allowed only in Development.");
        if (!string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail("Notifications:Email:Provider must be Console or Smtp.");
        if (string.IsNullOrWhiteSpace(options.Host) || options.Port is <= 0 or > 65535 ||
            string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.AuthenticationPassword) ||
            !MailAddress.TryCreate(options.FromAddress, out _) || !options.UseSsl)
            return ValidateOptionsResult.Fail("SMTP email delivery requires a host, valid port, username, password, valid sender address and TLS enabled.");
        return ValidateOptionsResult.Success;
    }
}

public sealed class SmsDeliveryOptions
{
    public const string SectionName = "Notifications:Sms";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "DRMC";
}

public sealed class SmtpEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        => await SendEmailAsync(email, subject, htmlMessage, CancellationToken.None);

    public async Task SendEmailAsync(string email, string subject, string htmlMessage, CancellationToken token)
        => await SendEmailAsync(email, subject, htmlMessage, null, token);

    public async Task SendEmailAsync(string email, string subject, string htmlMessage, string? plainTextBody, CancellationToken token)
    {
        var settings = options.Value;
        using var message = CreateMessage(email, subject, htmlMessage, plainTextBody);
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            Credentials = new NetworkCredential(settings.Username, settings.AuthenticationPassword)
        };
        await client.SendMailAsync(message, token);
    }

    internal MailMessage CreateMessage(string email, string subject, string htmlMessage, string? plainTextBody)
    {
        var message = new MailMessage(options.Value.FromAddress, email)
        {
            Subject = subject, SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8,
            Body = plainTextBody ?? htmlMessage, IsBodyHtml = plainTextBody is null
        };
        if (plainTextBody is not null)
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlMessage, Encoding.UTF8, MediaTypeNames.Text.Html));
        return message;
    }
}

public sealed class HttpSmsSender(HttpClient client, IOptions<SmsDeliveryOptions> options) : ISmsSender
{
    public async Task SendSmsAsync(string number, string message, CancellationToken token = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) throw new InvalidOperationException("SMS delivery is disabled.");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new { to = number, message, senderId = settings.SenderId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
    }
}
