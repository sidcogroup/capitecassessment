using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.Models;

/// <summary>
/// Tests for the rule that decides what a user may type into the sign-in box.
///
/// Two shapes are allowed:
///   - an account number (customers): exactly 10 digits, 0-9 only
///   - a username (consultants): 3-30 letters, digits, '.', '_' or '-', with at least one letter
/// Because a username must contain a letter, the two can never be confused.
/// </summary>
public class SignInIdentifierTests
{
    [Theory]
    // Valid
    [InlineData("1052000101", true)]                     // account number
    [InlineData("consultant1", true)]                    // username
    [InlineData("zanele.mokoena", true)]                 // username with a dot
    [InlineData("a_b-c", true)]                          // username with _ and -
    [InlineData("abc", true)]                            // shortest allowed username
    [InlineData("12345abcde", true)]                     // 10 characters but has letters: a username, not an account number
    // Invalid
    [InlineData("", false)]                              // empty
    [InlineData(null, false)]                            // missing
    [InlineData("ab", false)]                            // too short for a username
    [InlineData("123456789", false)]                     // 9 digits: too short for an account number, no letter so not a username
    [InlineData("12345678901", false)]                   // 11 digits: too long for an account number
    [InlineData("has space", false)]                     // spaces not allowed
    [InlineData("bad!name", false)]                      // '!' not allowed
    [InlineData("١٢٣٤٥٦٧٨٩٠", false)]                    // Arabic-Indic digits: look like numbers, but only 0-9 count
    [InlineData("1052000101\n", false)]                  // hidden trailing new line
    [InlineData("consultant1\n", false)]                 // hidden trailing new line
    [InlineData("1052000101' OR '1'='1", false)]         // SQL injection attempt
    [InlineData("admin'; DROP TABLE users;--", false)]   // SQL injection attempt
    [InlineData("1052000101;--", false)]                 // SQL comment
    public void IsValid_AcceptsAnAccountNumberOrAUsernameContainingALetter(string? value, bool expectedValid) =>
        Assert.Equal(expectedValid, SignInIdentifier.IsValid(value));

    /// <summary>Only exactly 10 digits counts as an account number; anything else is treated as a username.</summary>
    [Theory]
    [InlineData("1052000101", true)]
    [InlineData("12345abcde", false)]
    [InlineData("consultant1", false)]
    public void IsAccountNumber_IsTenAsciiDigitsOnly(string value, bool expectedIsAccountNumber) =>
        Assert.Equal(expectedIsAccountNumber, SignInIdentifier.IsAccountNumber(value));
}
