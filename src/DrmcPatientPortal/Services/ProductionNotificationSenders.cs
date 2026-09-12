using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace DrmcPatientPortal.Services;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "Notifications:Email";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
}

public sealed class SmsDeliveryOptions
{
    public const string SectionName = "Notifications:Sms";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "DRMC";
}

public sealed class SmtpEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var settings = options.Value;
        using var message = new MailMessage(settings.FromAddress, email, subject, htmlMessage) { IsBodyHtml = true };
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            Credentials = new NetworkCredential(settings.Username, settings.Password)
        };
        await client.SendMailAsync(message);
    }
}

public sealed class HttpSmsSender(HttpClient client, IOptions<SmsDeliveryOptions> options) : ISmsSender
{
    public async Task SendSmsAsync(string number, string message)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new { to = number, message, senderId = settings.SenderId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
