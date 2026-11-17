using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.DataAccess;

public interface IUserRepository
{
    /// <summary>The user's profile, or null when the user has no profile row.</summary>
    Task<UserProfileResponse?> GetProfileAsync(long userId, CancellationToken cancellationToken);
}
