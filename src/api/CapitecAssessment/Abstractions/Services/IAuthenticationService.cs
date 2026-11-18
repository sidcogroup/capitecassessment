using CapitecAssessment.Models;

namespace CapitecAssessment.Abstractions.Services;

public interface IAuthenticationService
{
    /// <summary>
    /// Looks up the user behind <paramref name="identifier"/> (a customer's account number or a
    /// consultant's username) and emails them a one-time pin.
    /// Completes silently when no such user exists, a code was sent too recently, or
    /// sending fails, so callers cannot tell those cases apart.
    /// </summary>
    Task RequestOtpAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Checks <paramref name="code"/> against the user's latest one-time pin and consumes it.
    /// Returns null when the user, code, expiry or attempt limit does not check out.
    /// </summary>
    Task<AuthenticatedUser?> VerifyOtpAsync(string identifier, string code, CancellationToken cancellationToken);
}
