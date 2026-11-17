namespace CapitecAssessment.Configuration;

/// <summary>
/// Per-client-IP limit on the sign-in endpoints, on top of the per-code attempt limit.
/// Bound from "RateLimiting:Authentication".
/// </summary>
public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting:Authentication";
    public const string PolicyName = "authentication";

    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}
