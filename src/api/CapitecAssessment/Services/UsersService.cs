using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class UsersService : IUsersService
{
    private readonly IUserRepository _users;

    public UsersService(IUserRepository users)
    {
        _users = users;
    }

    public Task<UserProfileResponse?> GetProfileAsync(long userId, CancellationToken cancellationToken) =>
        _users.GetProfileAsync(userId, cancellationToken);
}
