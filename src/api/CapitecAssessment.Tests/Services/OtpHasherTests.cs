using CapitecAssessment.Configuration;
using CapitecAssessment.Services;
using CapitecAssessment.Tests.Fakes;

namespace CapitecAssessment.Tests.Services;

public class OtpHasherTests
{
    private readonly OtpHasher _hasher = TestOtp.Hasher();

    [Fact]
    public void GenerateCode_IsSixDigits()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^[0-9]{6}$", OtpHasher.GenerateCode());
        }
    }

    [Fact]
    public void Hash_Is64HexCharsAndNotTheCode()
    {
        var hash = _hasher.Hash(1234567890, "123456");

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("123456", hash);
    }

    [Fact]
    public void Verify_AcceptsMatchingCodeAndUser()
    {
        var hash = _hasher.Hash(1234567890, "123456");

        Assert.True(_hasher.Verify(1234567890, "123456", hash));
    }

    [Fact]
    public void Verify_RejectsWrongCodeOrOtherUser()
    {
        var hash = _hasher.Hash(1234567890, "123456");

        Assert.False(_hasher.Verify(1234567890, "654321", hash));
        Assert.False(_hasher.Verify(1234567891, "123456", hash));
    }

    [Fact]
    public void Verify_RejectsHashMadeWithAnotherKey()
    {
        var otherKey = new OtpHasher(TestOtp.Options(o => o.HashKey = Convert.ToBase64String(new byte[32])));

        Assert.False(_hasher.Verify(1, "123456", otherKey.Hash(1, "123456")));
    }

    [Fact]
    public void Verify_RejectsMalformedStoredHash()
    {
        Assert.False(_hasher.Verify(1, "123456", "not-hex"));
    }

    [Theory]
    [InlineData("", 5, 3, false)]                 // no key
    [InlineData("AAAA", 5, 3, false)]             // key too short
    [InlineData(null, 6, 3, false)]               // validity over the 5 minute maximum
    [InlineData(null, 5, 0, false)]               // no attempts
    [InlineData(null, 5, 3, true)]
    public void OptionsValidator_EnforcesRules(string? key, int validityMinutes, int maxAttempts, bool valid)
    {
        var options = new OtpOptions { HashKey = key ?? TestOtp.HashKey, ValidityMinutes = validityMinutes, MaxAttempts = maxAttempts };

        var result = new OtpOptionsValidator().Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
    }
}
