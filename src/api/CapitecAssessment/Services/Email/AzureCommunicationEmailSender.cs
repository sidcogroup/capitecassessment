using Azure;
using Azure.Communication.Email;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Configuration;
using CapitecAssessment.Models;
using Microsoft.Extensions.Options;

namespace CapitecAssessment.Services.Email;

public class AzureCommunicationEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly AzureCommunicationEmailOptions _options;
    private readonly ILogger<AzureCommunicationEmailSender> _logger;

    public AzureCommunicationEmailSender(
        EmailClient client,
        IOptions<AzureCommunicationEmailOptions> options,
        ILogger<AzureCommunicationEmailSender> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var message = new EmailMessage(
            senderAddress: _options.SenderAddress,
            recipientAddress: email.Recipient,
            content: new EmailContent(email.Subject)
            {
                PlainText = email.PlainText,
                Html = email.Html
            });

        // WaitUntil.Started returns once ACS has accepted the message (invalid sender, recipient or
        // credentials still throw) without blocking sign-in on final delivery, which can take a while.
        var operation = await _client.SendAsync(WaitUntil.Started, message, cancellationToken);

        _logger.LogInformation("Email accepted by Azure Communication Services, operation {OperationId}", operation.Id);
    }
}
