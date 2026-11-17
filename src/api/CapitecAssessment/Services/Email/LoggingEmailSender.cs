using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models;

namespace CapitecAssessment.Services.Email;

/// <summary>
/// DEVELOPMENT ONLY: used when Azure Communication Services is not configured. Writes the
/// email (including the OTP) to the log so sign-in can be tested locally. Never registered
/// outside Development.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "[DEV EMAIL - Azure Communication Services not configured] To: {Recipient} | Subject: {Subject}\n{Body}",
            email.Recipient, email.Subject, email.PlainText);

        return Task.CompletedTask;
    }
}
