namespace DrmcPatientPortal.Services;

// Development SMS implementation that logs to console instead of dispatching via real telco SMS gateway.
// Documented in README.md as an infrastructure boundary.
public class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task SendSmsAsync(string number, string message)
    {
        logger.LogInformation(
            "[SMS GATEWAY BOUNDARY] Outgoing SMS to {Recipient}: {MessageText}",
            number,
            message);
        return Task.CompletedTask;
    }
}
