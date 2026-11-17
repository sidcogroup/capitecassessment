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
            await RecordAsync(userId, recipientEmail, status);
        }
    }

    private async Task RecordAsync(long userId, string recipient, string status)
    {
        try
        {
            await _notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationTypes.Otp,
                Channel = "Email",
                Recipient = recipient,
                Subject = OtpSubject,
                Body = OtpNotificationBody,
                Status = status
            }, CancellationToken.None); // record even if the request was aborted mid-send
        }
        catch (Exception ex)
        {
            // The audit row must never mask the outcome of the send itself.
            _logger.LogError(ex, "Failed to record OTP notification for user {UserId}", userId);
        }
    }
}
