using System.Text.RegularExpressions;

namespace CapitecAssessment.Models;

/// <summary>
/// What a user types to start signing in: a 10-digit account number (customers) or a username
/// (consultants). The two shapes never overlap, because a username must contain a letter.
/// </summary>
public static partial class SignInIdentifier
{
    public const string ErrorMessage =
        "Enter a 10-digit account number, or a username of 3-30 letters, digits, '.', '_' or '-' containing at least one letter.";

    // [0-9], not \d, so non-ASCII digits are rejected.
    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex AccountNumberPattern();

    [GeneratedRegex("^(?=.*[A-Za-z])[A-Za-z0-9._-]{3,30}$")]
    private static partial Regex UsernamePattern();

    public static bool IsAccountNumber(string value) => AccountNumberPattern().IsMatch(value);

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && (IsAccountNumber(value) || UsernamePattern().IsMatch(value));
}
