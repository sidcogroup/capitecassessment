using CapitecAssessment.Models;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CapitecAssessment.Tests.Services;

/// <summary>
/// Unit tests for the sign-in rules in <see cref="AuthenticationService"/> (no HTTP involved).
///
/// The rules being tested:
///   - A one-time pin (OTP) is 6 digits, emailed to the user, and valid for 5 minutes.
///   - Only a scrambled (hashed) copy of the pin is stored, never the pin itself.
///   - A new pin can be requested at most once every 60 seconds ("cooldown").
///   - Each pin allows 3 wrong guesses; after that it is locked, even for the correct pin.
///   - A pin can be used once only. Requesting a new pin cancels the old one.
///
/// The clock is fake (<see cref="FakeTimeProvider"/>), so tests can jump forward in time instantly
/// instead of really waiting 5 minutes.
/// </summary>
public class AuthenticationServiceTests
{
    private const string AccountNumber = "1234567890";
    private const string UnregisteredAccountNumber = "0000000000";
    private static readonly AuthenticatedUser User = new(5550001111, "thandi", UserRoles.Customer, "thandi@example.test");

    private static readonly TimeSpan PinValidFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private const int AllowedWrongGuesses = 3;

    private readonly FakeAuthenticationRepository _database = new();
    private readonly FakeMessagingService _emails = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        _database.AddAccount(AccountNumber, User);
        _service = new AuthenticationService(
            _database, _emails, TestOtp.Hasher(), TestOtp.Options(), _clock, NullLogger<AuthenticationService>.Instance);
    }

    /// <summary>Requests a pin for the test user and returns the pin that was "emailed".</summary>
    private async Task<string> RequestPinAsync()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        return _emails.Sent.Last().Code;
    }

    private Task<AuthenticatedUser?> VerifyAsync(string pin, string accountNumber = AccountNumber) =>
        _service.VerifyOtpAsync(accountNumber, pin, CancellationToken.None);

    /// <summary>Returns a pin that is guaranteed to be different from <paramref name="realPin"/>.</summary>
    private static string WrongPin(string realPin) => realPin == "000000" ? "000001" : "000000";

    // ---------------------------------------------------------------------------------------------
    // Requesting a pin
    // ---------------------------------------------------------------------------------------------

    /// <summary>An unregistered account number quietly does nothing: no error, no email, nothing stored.</summary>
    [Fact]
    public async Task RequestOtp_UnknownAccount_SendsNothingAndDoesNotThrow()
    {
        await _service.RequestOtpAsync(UnregisteredAccountNumber, CancellationToken.None);

        Assert.Empty(_emails.Sent);
        Assert.Empty(_database.Otps);
    }

    /// <summary>A registered account gets one email, to the user's address, with a 6-digit pin valid for 5 minutes.</summary>
    [Fact]
    public async Task RequestOtp_KnownAccount_SendsSixDigitCodeToUsersEmail()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        var sent = Assert.Single(_emails.Sent);
        Assert.Equal(User.UserId, sent.UserId);
        Assert.Equal(User.Email, sent.Recipient);
        Assert.Matches("^[0-9]{6}$", sent.Code);
        Assert.Equal(PinValidFor, sent.Validity);
    }

    /// <summary>
    /// The database holds only a hashed copy of the pin, so anyone reading the database cannot use it.
    /// The stored record expires in 5 minutes, is unused, and has had no guesses yet.
    /// </summary>
    [Fact]
    public async Task RequestOtp_StoresOnlyAHashThatExpiresInFiveMinutes()
    {
        var pin = await RequestPinAsync();

        var stored = Assert.Single(_database.Otps);
        Assert.NotEqual(pin, stored.CodeHash);
        Assert.DoesNotContain(pin, stored.CodeHash);
        Assert.Equal(_clock.Now.UtcDateTime.Add(PinValidFor), stored.ExpiresAt);
        Assert.Null(stored.ConsumedAt);
        Assert.Equal(0, stored.Attempts);
    }

    /// <summary>Asking again within 60 seconds does not send a second email (stops email spamming).</summary>
    [Fact]
    public async Task RequestOtp_WithinCooldown_DoesNotSendAnotherCode()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        _clock.Advance(Cooldown - TimeSpan.FromSeconds(1));

        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        Assert.Single(_emails.Sent);
        Assert.Single(_database.Otps);
    }

    /// <summary>Once 60 seconds have passed, a second request sends a new pin.</summary>
    [Fact]
    public async Task RequestOtp_AfterCooldown_SendsNewCode()
    {
        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);
        _clock.Advance(Cooldown + TimeSpan.FromSeconds(1));

        await _service.RequestOtpAsync(AccountNumber, CancellationToken.None);

        Assert.Equal(2, _emails.Sent.Count);
    }

    /// <summary>
    /// If the email fails to send, the service does not throw (the caller still gets the usual response),
    /// and the pin is still saved.
    /// </summary>
    [Fact]
    public async Task RequestOtp_SendFailure_IsSwallowed()
    {
        _emails.Fail = true;

        var exception = await Record.ExceptionAsync(() => _service.RequestOtpAsync(AccountNumber, CancellationToken.None));

        Assert.Null(exception);
        Assert.Single(_database.Otps);
    }

    // ---------------------------------------------------------------------------------------------
    // Verifying a pin
    // ---------------------------------------------------------------------------------------------

    /// <summary>The correct pin returns the user and marks the pin as used.</summary>
    [Fact]
    public async Task VerifyOtp_CorrectCode_ReturnsUserAndConsumesCode()
    {
        var pin = await RequestPinAsync();

        var result = await VerifyAsync(pin);

        Assert.Equal(User, result);
        Assert.NotNull(Assert.Single(_database.Otps).ConsumedAt);
    }

    /// <summary>A pin that has already been used once cannot be used again.</summary>
    [Fact]
    public async Task VerifyOtp_CodeCannotBeUsedTwice()
    {
        var pin = await RequestPinAsync();
        await VerifyAsync(pin);

        var secondUse = await VerifyAsync(pin);

        Assert.Null(secondUse);
    }

    /// <summary>One second before the 5 minutes are up, the pin still works.</summary>
    [Fact]
    public async Task VerifyOtp_JustBeforeExpiry_Succeeds()
    {
        var pin = await RequestPinAsync();
        _clock.Advance(PinValidFor - TimeSpan.FromSeconds(1));

        Assert.NotNull(await VerifyAsync(pin));
    }

    /// <summary>At exactly 5 minutes, the pin has expired.</summary>
    [Fact]
    public async Task VerifyOtp_AfterFiveMinutes_Fails()
    {
        var pin = await RequestPinAsync();
        _clock.Advance(PinValidFor);

        Assert.Null(await VerifyAsync(pin));
    }

    /// <summary>A wrong pin fails and is counted as one used-up guess.</summary>
    [Fact]
    public async Task VerifyOtp_WrongCode_FailsAndCountsAttempt()
    {
        var pin = await RequestPinAsync();

        var result = await VerifyAsync(WrongPin(pin));

        Assert.Null(result);
        Assert.Equal(1, Assert.Single(_database.Otps).Attempts);
    }

    /// <summary>After 3 wrong guesses the pin is locked: even the correct pin is now refused (stops guessing attacks).</summary>
    [Fact]
    public async Task VerifyOtp_LockedAfterThreeWrongAttempts_EvenWithCorrectCode()
    {
        var pin = await RequestPinAsync();
        for (var guess = 0; guess < AllowedWrongGuesses; guess++)
        {
            Assert.Null(await VerifyAsync(WrongPin(pin)));
        }

        Assert.Null(await VerifyAsync(pin));
    }

    /// <summary>Two wrong guesses are allowed: the correct pin on the third try still works.</summary>
    [Fact]
    public async Task VerifyOtp_TwoWrongThenCorrect_Succeeds()
    {
        var pin = await RequestPinAsync();
        await VerifyAsync(WrongPin(pin));
        await VerifyAsync(WrongPin(pin));

        Assert.NotNull(await VerifyAsync(pin));
    }

    /// <summary>Requesting a new pin cancels the previous one: only the newest pin works.</summary>
    [Fact]
    public async Task VerifyOtp_NewCodeInvalidatesPreviousOne()
    {
        var firstPin = await RequestPinAsync();
        _clock.Advance(Cooldown + TimeSpan.FromSeconds(1));
        var secondPin = await RequestPinAsync();

        // The two random pins are the same about 1 time in a million; then this check cannot tell them apart.
        if (firstPin != secondPin)
        {
            Assert.Null(await VerifyAsync(firstPin));
        }
        Assert.NotNull(await VerifyAsync(secondPin));
    }

    /// <summary>A real pin is useless with a different (unregistered) account number.</summary>
    [Fact]
    public async Task VerifyOtp_UnknownAccount_Fails()
    {
        var pin = await RequestPinAsync();

        Assert.Null(await VerifyAsync(pin, accountNumber: UnregisteredAccountNumber));
    }

    /// <summary>Verifying when no pin was ever requested fails.</summary>
    [Fact]
    public async Task VerifyOtp_NoCodeRequested_Fails()
    {
        Assert.Null(await VerifyAsync("123456"));
    }

    /// <summary>
    /// If the correct pin is sent 5 times at the same moment (e.g. a double-clicked button, or an attacker
    /// racing the real user), exactly one of them signs in.
    /// </summary>
    [Fact]
    public async Task VerifyOtp_ConcurrentCorrectSubmissions_OnlyOneSucceeds()
    {
        var pin = await RequestPinAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => VerifyAsync(pin))));

        Assert.Single(results, signedInUser => signedInUser is not null);
    }
}
