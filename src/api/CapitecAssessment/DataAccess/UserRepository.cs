using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<UserProfileResponse?> GetProfileAsync(long userId, CancellationToken cancellationToken)
    {
        // user_profiles.user_id is the primary key.
        return await _db.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new UserProfileResponse
            {
                UserId = p.UserId,
                FirstName = p.FirstName,
                LastName = p.LastName
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
