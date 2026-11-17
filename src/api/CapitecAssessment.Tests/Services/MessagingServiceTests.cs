using CapitecAssessment.Models;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

public class MessagingServiceTests
{
    private readonly FakeEmailSender _sender = new();
    private readonly FakeNotificationRepository _notifications = new();
    private readonly MessagingService _service;

    public MessagingServiceTests()
    {
        _service = new MessagingService(_sender, _notifications, NullLogger<MessagingService>.Instance);
    }

    [Fact]
    public async Task SendOtp_EmailsCodeToRecipient()
    {
        await _service.SendOtpAsync(42, "user@example.test", "482913", TimeSpan.FromMinutes(5), CancellationToken.None);

        var email = Assert.Single(_sender.Sent);
        Assert.Equal("user@example.test", email.Recipient);
        Assert.Equal(MessagingService.OtpSubject, email.Subject);
        Assert.Contains("482913", email.PlainText);
        Assert.Contains("482913", email.Html);
        Assert.Contains("5 minutes", email.PlainText);
    }

    [Fact]
    public async Task SendOtp_RecordsSentNotificationWithoutTheCode()
    {
        await _service.SendOtpAsync(42, "user@example.test", "482913", TimeSpan.FromMinutes(5), CancellationToken.None);

        var notification = Assert.Single(_notifications.Added);
        Assert.Equal(42, notification.UserId);
        Assert.Equal(NotificationTypes.Otp, notification.Type);
        Assert.Equal(NotificationStatuses.Sent, notification.Status);
        Assert.Equal("user@example.test", notification.Recipient);
        Assert.DoesNotContain("482913", notification.Body);
        Assert.DoesNotContain("482913", notification.Subject);
    }

    [Fact]
    public async Task SendOtp_TransportFailure_RecordsFailedNotificationAndRethrows()
    {
        _sender.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendOtpAsync(42, "user@example.test", "482913", TimeSpan.FromMinutes(5), CancellationToken.None));

        Assert.Equal(NotificationStatuses.Failed, Assert.Single(_notifications.Added).Status);
    }
}
