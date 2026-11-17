using CapitecAssessment.Models;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

public class AuthenticationServiceTests
{
    private const string AccountNumber = "1234567890";
    private static readonly AuthenticatedUser User = new(5550001111, "thandi", UserRoles.Customer, "thandi@example.test");

    private readonly FakeAuthenticationRepository _repository = new();
    private readonly FakeMessagingService _messaging = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        _repository.AddAccount(AccountNumber, User);
        _service = new AuthenticationService(
            _repository, _messaging, TestOtp.Hasher(), TestOtp.Options(), _time, NullLogger<AuthenticationService>.Instance);
    }

    private async Task<string> RequestCodeAsync()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        return _messaging.Sent.Last().Code;
    }

    // ----- RequestOtpAsync -----

    [Fact]
    public async Task RequestOtp_UnknownAccount_SendsNothingAndDoesNotThrow()
    {
        await _service.RequestOtpAsync("0000000000", CancellationToken.None);

        Assert.Empty(_messaging.Sent);
        Assert.Empty(_repository.Otps);
    }

    [Fact]
    public async Task RequestOtp_KnownAccount_SendsSixDigitCodeToUsersEmail()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        var sent = Assert.Single(_messaging.Sent);
        Assert.Equal(User.UserId, sent.UserId);
        Assert.Equal(User.Email, sent.Recipient);
        Assert.Matches("^[0-9]{6}$", sent.Code);
        Assert.Equal(TimeSpan.FromMinutes(5), sent.Validity);
    }

    [Fact]
    public async Task RequestOtp_StoresOnlyAHashThatExpiresInFiveMinutes()
    {
        var code = await RequestCodeAsync();

        var otp = Assert.Single(_repository.Otps);
        Assert.NotEqual(code, otp.CodeHash);
        Assert.DoesNotContain(code, otp.CodeHash);
        Assert.Equal(_time.Now.UtcDateTime.AddMinutes(5), otp.ExpiresAt);
        Assert.Null(otp.ConsumedAt);
        Assert.Equal(0, otp.Attempts);
    }

    [Fact]
    public async Task RequestOtp_WithinCooldown_DoesNotSendAnotherCode()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(59));

        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        Assert.Single(_messaging.Sent);
        Assert.Single(_repository.Otps);
    }

    [Fact]
    public async Task RequestOtp_AfterCooldown_SendsNewCode()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(61));

        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        Assert.Equal(2, _messaging.Sent.Count);
    }

    [Fact]
    public async Task RequestOtp_SendFailure_IsSwallowed()
    {
        _messaging.Fail = true;

        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None); // must not throw

        Assert.Single(_repository.Otps);
    }

    // ----- VerifyOtpAsync -----

    [Fact]
    public async Task VerifyOtp_CorrectCode_ReturnsUserAndConsumesCode()
    {
        var code = await RequestCodeAsync();

        var result = await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None);

        Assert.Equal(User, result);
        Assert.NotNull(Assert.Single(_repository.Otps).ConsumedAt);
    }

    [Fact]
    public async Task VerifyOtp_CodeCannotBeUsedTwice()
    {
        var code = await RequestCodeAsync();
        await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None);

        var second = await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task VerifyOtp_JustBeforeExpiry_Succeeds()
    {
        var code = await RequestCodeAsync();
        _time.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));

        Assert.NotNull(await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_AfterFiveMinutes_Fails()
    {
        var code = await RequestCodeAsync();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_WrongCode_FailsAndCountsAttempt()
    {
        var code = await RequestCodeAsync();

        var result = await _service.VerifyOtpAsync(AccountNumber, WrongCode(code), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, Assert.Single(_repository.Otps).Attempts);
    }

    [Fact]
    public async Task VerifyOtp_LockedAfterThreeWrongAttempts_EvenWithCorrectCode()
    {
        var code = await RequestCodeAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await _service.VerifyOtpAsync(AccountNumber, WrongCode(code), CancellationToken.None));
        }

        Assert.Null(await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_TwoWrongThenCorrect_Succeeds()
    {
        var code = await RequestCodeAsync();
        await _service.VerifyOtpAsync(AccountNumber, WrongCode(code), CancellationToken.None);
        await _service.VerifyOtpAsync(AccountNumber, WrongCode(code), CancellationToken.None);

        Assert.NotNull(await _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_NewCodeInvalidatesPreviousOne()
    {
        var first = await RequestCodeAsync();
        _time.Advance(TimeSpan.FromSeconds(61));
        var second = await RequestCodeAsync();

        if (first != second) // 1-in-a-million chance they collide
        {
            Assert.Null(await _service.VerifyOtpAsync(AccountNumber, first, CancellationToken.None));
        }
        Assert.NotNull(await _service.VerifyOtpAsync(AccountNumber, second, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_UnknownAccount_Fails()
    {
        var code = await RequestCodeAsync();

        Assert.Null(await _service.VerifyOtpAsync("0000000000", code, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_NoCodeRequested_Fails()
    {
        Assert.Null(await _service.VerifyOtpAsync(AccountNumber, "123456", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyOtp_ConcurrentCorrectSubmissions_OnlyOneSucceeds()
    {
        var code = await RequestCodeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => _service.VerifyOtpAsync(AccountNumber, code, CancellationToken.None))));

        Assert.Single(results, r => r is not null);
    }

    private static string WrongCode(string code) => code == "000000" ? "000001" : "000000";
}
