using System.Net;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models;

namespace CapitecAssessment.Services;

/// <summary>Composes user-facing messages, hands them to the email transport and logs them in cadb.notifications.</summary>
public class MessagingService : IMessagingService
{
    public const string OtpSubject = "Your sign-in one-time pin";

    /// <summary>Stored in notifications.body instead of the real content: the pin itself is never persisted.</summary>
    public const string OtpNotificationBody = "One-time pin sent (code withheld).";

    private readonly IEmailSender _emailSender;
    private readonly INotificationRepository _notifications;
    private readonly ILogger<MessagingService> _logger;

    public MessagingService(IEmailSender emailSender, INotificationRepository notifications, ILogger<MessagingService> logger)
    {
        _emailSender = emailSender;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task SendOtpAsync(long userId, string recipientEmail, string code, TimeSpan validity, CancellationToken cancellationToken)
    {
        var minutes = (int)Math.Ceiling(validity.TotalMinutes);
        var email = new OutgoingEmail(
            recipientEmail,
            OtpSubject,
            PlainText:
                $"Your one-time pin is {code}.\n\n" +
                $"It expires in {minutes} minutes and can only be used once.\n" +
                "Never share this pin with anyone. The bank will never ask you for it.\n" +
                "If you did not try to sign in, you can ignore this email.",
            Html:
                $"<p>Your one-time pin is:</p><p style=\"font-size:28px;font-weight:bold;letter-spacing:6px\">{WebUtility.HtmlEncode(code)}</p>" +
                $"<p>It expires in {minutes} minutes and can only be used once.</p>" +
                "<p>Never share this pin with anyone. The bank will never ask you for it.</p>" +
                "<p>If you did not try to sign in, you can ignore this email.</p>");

        var status = NotificationStatuses.Sent;
        try
        {
            await _emailSender.SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status = NotificationStatuses.Failed;
            _logger.LogError(ex, "Sending OTP email to user {UserId} failed", userId);
            throw;
        }
        finally
        {
            await RecordAsync(userId, NotificationTypes.Otp, recipientEmail, OtpSubject, OtpNotificationBody, null, status);
        }
    }

    public async Task SendDisputeUpdateAsync(
        long userId, string recipientEmail, Guid disputeId, string disputeReference, string disputeStatus, string note,
        CancellationToken cancellationToken)
    {
        var subject = $"Update on your dispute {disputeReference}: {disputeStatus}";
        var plainText =
            $"Your dispute {disputeReference} is now: {disputeStatus}.\n\n" +
            $"Note from your consultant:\n{note}\n\n" +
            "You can view the full history of this dispute in the app.";
        var email = new OutgoingEmail(
            recipientEmail,
            subject,
            PlainText: plainText,
            Html:
                $"<p>Your dispute <strong>{WebUtility.HtmlEncode(disputeReference)}</strong> is now: " +
                $"<strong>{WebUtility.HtmlEncode(disputeStatus)}</strong>.</p>" +
                $"<p>Note from your consultant:</p><blockquote>{WebUtility.HtmlEncode(note)}</blockquote>" +
                "<p>You can view the full history of this dispute in the app.</p>");

        var type = disputeStatus == DisputeStatuses.UnderReview
            ? NotificationTypes.DisputeProgress
            : NotificationTypes.DisputeFinalised;

        var status = NotificationStatuses.Sent;
        try
        {
            await _emailSender.SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status = NotificationStatuses.Failed;
            _logger.LogError(ex, "Sending dispute update email to user {UserId} failed", userId);
            throw;
        }
        finally
        {
            await RecordAsync(userId, type, recipientEmail, subject, Truncate(plainText, 2000), disputeId, status);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private async Task RecordAsync(
        long userId, string type, string recipient, string subject, string body, Guid? disputeId, string status)
    {
        try
        {
            await _notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = type,
                Channel = "Email",
                Recipient = recipient,
                Subject = Truncate(subject, 200),
                Body = body,
                Status = status,
                DisputeId = disputeId
            }, CancellationToken.None); // record even if the request was aborted mid-send
        }
        catch (Exception ex)
        {
            // The audit row must never mask the outcome of the send itself.
            _logger.LogError(ex, "Failed to record {NotificationType} notification for user {UserId}", type, userId);
        }
    }
}
