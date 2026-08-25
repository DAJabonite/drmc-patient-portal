using Microsoft.AspNetCore.Identity.UI.Services;

namespace DrmcPatientPortal.Services;

// No SMTP provider is wired for this build. In Development, emails (e.g. the
// password-reset link) are written to the application log/console so the flow
// can be exercised end-to-end. Documented as a known limitation in the README;
// nothing "demo"-like is surfaced in any rendered UI.
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
