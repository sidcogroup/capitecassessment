using CapitecAssessment.Configuration;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;

namespace CapitecAssessment.Tests.Services;

/// <summary>
/// Tests for <see cref="OtpHasher"/>, which creates one-time pins and scrambles ("hashes") them before storage.
///
/// A hash is a one-way fingerprint: you can check a pin against it, but you cannot get the pin back out.
/// The hash mixes in the user's ID and a secret key, so a stored hash only matches that exact pin,
/// for that exact user, on a server that has that key.
/// </summary>
public class OtpHasherTests
{
    private const long UserId = 1234567890;
    private const long DifferentUserId = 1234567891;
    private const string Pin = "123456";

    private readonly OtpHasher _hasher = TestOtp.Hasher();

    /// <summary>Generated pins are always exactly 6 digits (checked 200 times, since pins are random).</summary>
    [Fact]
    public void GenerateCode_IsSixDigits()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^[0-9]{6}$", OtpHasher.GenerateCode());
        }
    }

    /// <summary>The hash is 64 hex characters and does not contain the pin in readable form.</summary>
    [Fact]
    public void Hash_Is64HexCharsAndNotTheCode()
    {
        var hash = _hasher.Hash(UserId, Pin);

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain(Pin, hash);
    }

    /// <summary>The same pin for the same user matches its hash.</summary>
    [Fact]
    public void Verify_AcceptsMatchingCodeAndUser()
    {
        var hash = _hasher.Hash(UserId, Pin);

        Assert.True(_hasher.Verify(UserId, Pin, hash));
    }

    /// <summary>A different pin does not match, and neither does the right pin for a different user.</summary>
    [Fact]
    public void Verify_RejectsWrongCodeOrOtherUser()
    {
        var hash = _hasher.Hash(UserId, Pin);

        Assert.False(_hasher.Verify(UserId, "654321", hash));
        Assert.False(_hasher.Verify(DifferentUserId, Pin, hash));
    }

    /// <summary>A hash made with a different secret key does not match, so stolen hashes are useless elsewhere.</summary>
    [Fact]
    public void Verify_RejectsHashMadeWithAnotherKey()
    {
        var allZeroKey = Convert.ToBase64String(new byte[32]);
        var hasherWithOtherKey = new OtpHasher(TestOtp.Options(o => o.HashKey = allZeroKey));

        var hashFromOtherServer = hasherWithOtherKey.Hash(UserId, Pin);

        Assert.False(_hasher.Verify(UserId, Pin, hashFromOtherServer));
    }

    /// <summary>A corrupted stored hash simply fails to match (it does not crash).</summary>
    [Fact]
    public void Verify_RejectsMalformedStoredHash()
    {
        Assert.False(_hasher.Verify(UserId, Pin, "not-hex"));
    }

    /// <summary>
    /// The OTP settings are checked at start-up. Bad settings stop the app from starting
    /// instead of silently running insecurely. (A null key below means "use a valid test key".)
    /// </summary>
    [Theory]
    [InlineData("", 5, 3, false)]       // no secret key
    [InlineData("AAAA", 5, 3, false)]   // secret key too short
    [InlineData(null, 6, 3, false)]     // pin valid for 6 minutes: over the 5 minute maximum
    [InlineData(null, 5, 0, false)]     // zero guesses allowed
    [InlineData(null, 5, 3, true)]      // all good
    public void OptionsValidator_EnforcesRules(string? key, int validityMinutes, int maxAttempts, bool expectedValid)
    {
        var options = new OtpOptions { HashKey = key ?? TestOtp.HashKey, ValidityMinutes = validityMinutes, MaxAttempts = maxAttempts };

        var result = new OtpOptionsValidator().Validate(null, options);

        Assert.Equal(expectedValid, result.Succeeded);
    }
}
