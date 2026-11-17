using CapitecAssessment.Models;

namespace CapitecAssessment.Abstractions.Services;

public interface IAuthenticationService
{
    /// <summary>
    /// Looks up the user behind <paramref name="accountNumber"/> and emails them a one-time pin.
    /// Completes silently when the account does not exist, a code was sent too recently, or
    /// sending fails, so callers cannot tell those cases apart.
    /// </summary>
    Task RequestOtpAsync(string accountNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Checks <paramref name="code"/> against the user's latest one-time pin and consumes it.
    /// Returns null when the account, code, expiry or attempt limit does not check out.
    /// </summary>
    Task<AuthenticatedUser?> VerifyOtpAsync(string accountNumber, string code, CancellationToken cancellationToken);
}
