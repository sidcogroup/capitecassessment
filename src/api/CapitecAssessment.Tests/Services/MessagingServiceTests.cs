using CapitecAssessment.Models;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

/// <summary>
/// Tests for <see cref="MessagingService"/>, which builds and sends the one-time pin email,
/// and saves a notification record of every email it tries to send (Sent or Failed).
/// </summary>
public class MessagingServiceTests
{
    private const long UserId = 42;
    private const string Recipient = "user@example.test";
    private const string Pin = "482913";
    private static readonly TimeSpan PinValidFor = TimeSpan.FromMinutes(5);

    private readonly FakeEmailSender _inbox = new();
    private readonly FakeNotificationRepository _notifications = new();
    private readonly MessagingService _service;

    public MessagingServiceTests()
    {
        _service = new MessagingService(_inbox, _notifications, NullLogger<MessagingService>.Instance);
    }

    private Task SendPinEmail() =>
        _service.SendOtpAsync(UserId, Recipient, Pin, PinValidFor, CancellationToken.None);

    /// <summary>The email goes to the right person, with the pin and how long it is valid for, in both the plain and HTML versions.</summary>
    [Fact]
    public async Task SendOtp_EmailsCodeToRecipient()
    {
        await SendPinEmail();

        var email = Assert.Single(_inbox.Sent);
        Assert.Equal(Recipient, email.Recipient);
        Assert.Equal(MessagingService.OtpSubject, email.Subject);
        Assert.Contains(Pin, email.PlainText);
        Assert.Contains(Pin, email.Html);
        Assert.Contains("5 minutes", email.PlainText);
    }

    /// <summary>
    /// A notification record is saved as "Sent". Security: the pin itself must NOT be saved in the record,
    /// because anyone reading the notifications table could use it to sign in.
    /// </summary>
    [Fact]
    public async Task SendOtp_RecordsSentNotificationWithoutTheCode()
    {
        await SendPinEmail();

        var notification = Assert.Single(_notifications.Added);
        Assert.Equal(UserId, notification.UserId);
        Assert.Equal(NotificationTypes.Otp, notification.Type);
        Assert.Equal(NotificationStatuses.Sent, notification.Status);
        Assert.Equal(Recipient, notification.Recipient);
        Assert.DoesNotContain(Pin, notification.Body);
        Assert.DoesNotContain(Pin, notification.Subject);
    }

    /// <summary>If the email provider fails, the record is saved as "Failed" and the error is passed on to the caller.</summary>
    [Fact]
    public async Task SendOtp_TransportFailure_RecordsFailedNotificationAndRethrows()
    {
        _inbox.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(SendPinEmail);

        Assert.Equal(NotificationStatuses.Failed, Assert.Single(_notifications.Added).Status);
    }
}
