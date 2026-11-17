using CapitecAssessment.Models;

namespace CapitecAssessment.Abstractions.Services;

/// <summary>Transport only: delivers an already-composed email (Azure Communication Services, or the log in Development).</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}
