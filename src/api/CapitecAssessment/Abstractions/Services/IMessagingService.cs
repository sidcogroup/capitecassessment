namespace CapitecAssessment.Abstractions.Services;

public interface IMessagingService
{
    /// <summary>Emails a one-time pin to the user and records the notification. Throws if sending fails.</summary>
    Task SendOtpAsync(long userId, string recipientEmail, string code, TimeSpan validity, CancellationToken cancellationToken);
}
