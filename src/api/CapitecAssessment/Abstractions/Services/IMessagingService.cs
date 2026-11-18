namespace CapitecAssessment.Abstractions.Services;

public interface IMessagingService
{
    /// <summary>Emails a one-time pin to the user and records the notification. Throws if sending fails.</summary>
    Task SendOtpAsync(long userId, string recipientEmail, string code, TimeSpan validity, CancellationToken cancellationToken);

    /// <summary>
    /// Emails the client the new status of their dispute and the consultant's note, and records the
    /// notification (DisputeProgress for Under Review, DisputeFinalised for Resolved / Rejected). Throws if sending fails.
    /// </summary>
    Task SendDisputeUpdateAsync(
        long userId, string recipientEmail, Guid disputeId, string disputeReference, string status, string note,
        CancellationToken cancellationToken);
}
