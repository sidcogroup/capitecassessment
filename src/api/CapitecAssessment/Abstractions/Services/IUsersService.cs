using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

/// <summary>User management: anything about who a user is, as opposed to how they sign in.</summary>
public interface IUsersService
{
    /// <summary>The user's profile, or null when none exists.</summary>
    Task<UserProfileResponse?> GetProfileAsync(long userId, CancellationToken cancellationToken);
}
