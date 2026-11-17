using Microsoft.Extensions.Options;

namespace CapitecAssessment.Configuration;

/// <summary>One-time pin rules. Bound from the "Otp" configuration section.</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Codes are always 6 digits; request validation depends on it.</summary>
    public const int CodeLength = 6;

    /// <summary>Business rule: a code is never valid for longer than this.</summary>
    public const int MaxValidityMinutes = 5;

    /// <summary>
    /// Base64 secret (at least 32 bytes) used to HMAC the code before it is stored, so a leaked
    /// otp_codes table cannot be brute-forced offline. Set via user-secrets / environment variable
    /// (Otp__HashKey). In Development an ephemeral key is generated when it is not set.
    /// </summary>
    public string HashKey { get; set; } = string.Empty;

    public int ValidityMinutes { get; set; } = MaxValidityMinutes;

    /// <summary>Verification attempts allowed per code (including the successful one).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Minimum gap between two codes for the same user.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// LOCAL TESTING ONLY: writes the plain code to the application log. Must stay false outside
    /// appsettings.Development.json.
    /// </summary>
    public bool LogCodeToConsole { get; set; }

    public TimeSpan Validity => TimeSpan.FromMinutes(ValidityMinutes);
    public TimeSpan ResendCooldown => TimeSpan.FromSeconds(ResendCooldownSeconds);
}

public sealed class OtpOptionsValidator : IValidateOptions<OtpOptions>
{
    public ValidateOptionsResult Validate(string? name, OtpOptions options)
    {
        var failures = new List<string>();

        if (!TryDecodeKey(options.HashKey, out var key) || key.Length < 32)
        {
            failures.Add("Otp:HashKey must be a base64 string of at least 32 bytes.");
        }

        if (options.ValidityMinutes is < 1 or > OtpOptions.MaxValidityMinutes)
        {
            failures.Add($"Otp:ValidityMinutes must be between 1 and {OtpOptions.MaxValidityMinutes}.");
        }

        if (options.MaxAttempts is < 1 or > 10)
        {
            failures.Add("Otp:MaxAttempts must be between 1 and 10.");
        }

        if (options.ResendCooldownSeconds < 0)
        {
            failures.Add("Otp:ResendCooldownSeconds cannot be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool TryDecodeKey(string value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
