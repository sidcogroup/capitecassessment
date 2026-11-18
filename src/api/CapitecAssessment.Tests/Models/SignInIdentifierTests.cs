using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.Models;

public class SignInIdentifierTests
{
    [Theory]
    [InlineData("1052000101", true)]    // account number
    [InlineData("consultant1", true)]   // username
    [InlineData("zanele.mokoena", true)]
    [InlineData("a_b-c", true)]
    [InlineData("abc", true)]
    [InlineData("12345abcde", true)]    // 10 characters but not all digits: a username, not an account number
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("ab", false)]           // too short
    [InlineData("123456789", false)]    // 9 digits: no letter, so not a username
    [InlineData("12345678901", false)]  // 11 digits
    [InlineData("has space", false)]
    [InlineData("bad!name", false)]
    [InlineData("١٢٣٤٥٦٧٨٩٠", false)]   // Arabic-Indic digits
    [InlineData("1052000101\n", false)] // trailing newline (would pass with $)
    [InlineData("consultant1\n", false)]
    [InlineData("1052000101' OR '1'='1", false)]
    [InlineData("admin'; DROP TABLE users;--", false)]
    [InlineData("1052000101;--", false)]
    public void IsValid_AcceptsAnAccountNumberOrAUsernameContainingALetter(string? value, bool expected) =>
        Assert.Equal(expected, SignInIdentifier.IsValid(value));

    [Theory]
    [InlineData("1052000101", true)]
    [InlineData("12345abcde", false)]
    [InlineData("consultant1", false)]
    public void IsAccountNumber_IsTenAsciiDigitsOnly(string value, bool expected) =>
        Assert.Equal(expected, SignInIdentifier.IsAccountNumber(value));
}
