using System.Security.Cryptography;
using System.Text;
using CapitecAssessment.Configuration;
using Microsoft.Extensions.Options;

namespace CapitecAssessment.Services;

/// <summary>
/// HMAC-SHA256 of the code keyed with a server secret. A plain hash of a 6-digit code could be
/// reversed by trying all 1,000,000 values; without the key that is not possible.
/// The user id is mixed in so the same code for two users never produces the same hash.
/// </summary>
public sealed class OtpHasher
{
    private readonly byte[] _key;

    public OtpHasher(IOptions<OtpOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.HashKey);
    }

    public static string GenerateCode()
    {
        // Cryptographically secure and uniform over 000000-999999.
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    /// <returns>64 lowercase hex characters (fits otp_codes.code_hash).</returns>
    public string Hash(long userId, string code)
    {
        return Convert.ToHexStringLower(ComputeHash(userId, code));
    }

    public bool Verify(long userId, string code, string storedHash)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ComputeHash(userId, code), expected);
    }

    private byte[] ComputeHash(long userId, string code)
    {
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{userId}:{code}"));
    }
}
