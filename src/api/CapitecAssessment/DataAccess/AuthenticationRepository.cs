using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

public class AuthenticationRepository : IAuthenticationRepository
{
    private readonly ApplicationDbContext _db;

    public AuthenticationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<AuthenticatedUser?> FindUserByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        // The OTP goes to the contact email when there is one, otherwise the login email.
        if (!SignInIdentifier.IsAccountNumber(identifier))
        {
            // A username signs in a consultant only. A customer's username must never work here,
            // otherwise it would bypass the account-number step; it resolves to "not found" like any unknown value.
            // users.username is unique.
            return _db.Users
                .AsNoTracking()
                .Where(u => u.Username == identifier && u.Role == UserRoles.Consultant)
                .Select(u => new AuthenticatedUser(
                    u.Id,
                    u.Username,
                    u.Role,
                    u.Contact != null ? u.Contact.ContactEmail : u.Email))
                .SingleOrDefaultAsync(cancellationToken);
        }

        // accounts.account_number is unique; any of a user's accounts resolves to that user.
        // Only Customers sign in this way, so a consultant who also owns an account cannot.
        return _db.Accounts
            .AsNoTracking()
            .Where(a => a.AccountNumber == identifier && a.User!.Role == UserRoles.Customer)
            .Select(a => new AuthenticatedUser(
                a.User!.Id,
                a.User.Username,
                a.User.Role,
                a.User.Contact != null ? a.User.Contact.ContactEmail : a.User.Email))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<OtpCode?> GetLatestOtpAsync(long userId, CancellationToken cancellationToken)
    {
        // Served by ix_otp_user (user_id, created_at DESC).
        return _db.OtpCodes
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddOtpAsync(OtpCode otp, CancellationToken cancellationToken)
    {
        _db.OtpCodes.Add(otp);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryReserveAttemptAsync(Guid otpId, int maxAttempts, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Single conditional UPDATE, so concurrent guesses cannot exceed maxAttempts.
        var updated = await _db.OtpCodes
            .Where(o => o.Id == otpId
                        && o.ConsumedAt == null
                        && o.ExpiresAt > nowUtc
                        && o.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Attempts, o => (short)(o.Attempts + 1)), cancellationToken);

        return updated == 1;
    }

    public async Task<bool> TryConsumeAsync(Guid otpId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var updated = await _db.OtpCodes
            .Where(o => o.Id == otpId && o.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConsumedAt, nowUtc), cancellationToken);

        return updated == 1;
    }
}
