using Microsoft.AspNetCore.Identity.UI.Services;

namespace DrmcPatientPortal.Services;

// Default Development transport. Select Notifications:Email:Provider=Smtp to
// deliver real email using locally configured SMTP credentials instead.
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger = logger;

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        _logger.LogInformation(
            "[EMAIL OUTBOX] To: {To} | Subject: {Subject} | Body: {Body}",
            email, subject, htmlMessage);
        return Task.CompletedTask;
    }
}
