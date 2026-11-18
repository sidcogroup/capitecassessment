using CapitecAssessment.Models;

namespace CapitecAssessment.Abstractions.DataAccess;

public interface IAuthenticationRepository
{
    /// <summary>
    /// Resolves a 10-digit account number to its Customer, or any other value to the Consultant with that
    /// username. Returns null when nothing matches; a Customer's username never matches.
    /// </summary>
    Task<AuthenticatedUser?> FindUserByIdentifierAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>The most recently created code for the user, whatever its state. Only this one can be verified.</summary>
    Task<OtpCode?> GetLatestOtpAsync(long userId, CancellationToken cancellationToken);

    Task AddOtpAsync(OtpCode otp, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically uses up one attempt, but only while the code is unconsumed, unexpired and under
    /// <paramref name="maxAttempts"/>. Returns false when no attempt was available.
    /// </summary>
    Task<bool> TryReserveAttemptAsync(Guid otpId, int maxAttempts, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Atomically marks the code consumed. Returns false if it was already consumed.</summary>
    Task<bool> TryConsumeAsync(Guid otpId, DateTime nowUtc, CancellationToken cancellationToken);
}
