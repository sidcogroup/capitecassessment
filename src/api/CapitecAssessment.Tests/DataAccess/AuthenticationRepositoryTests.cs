using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="AuthenticationRepository"/> against a REAL PostgreSQL database.
///
/// Why a real database: the "use a pin only once" and "count wrong guesses" rules rely on SQL
/// conditional UPDATEs (update only if still unused / still under the limit). Those guarantees only
/// hold in a real database.
///
/// Each test runs inside a database transaction that is never committed, so nothing is left behind.
/// Skipped unless the CAPITEC_TEST_CONNECTION environment variable is set (see <see cref="PostgresDbTests"/>).
/// </summary>
public class AuthenticationRepositoryTests
{
    /// <summary>
    /// Saves a new customer with one account. If <paramref name="withContact"/> is true, the user also gets a
    /// separate contact email starting with "contact-" (used to tell the two email addresses apart in tests).
    /// </summary>
    private static async Task<(ApplicationDbContext Db, User User, string AccountNumber)> SeedAsync(bool withContact)
    {
        var db = PostgresTestDb.NewContext();
        await db.Database.BeginTransactionAsync(); // rolled back automatically when the context is disposed

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new User { Email = $"{suffix}@example.test", Username = $"u{suffix}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (withContact)
        {
            db.UserContacts.Add(new UserContact { UserId = user.Id, ContactNumber = "+27820000000", ContactEmail = $"contact-{suffix}@example.test" });
        }

        var accountNumber = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString();
        db.Accounts.Add(new Account { UserId = user.Id, AccountNumber = accountNumber, AccountType = AccountTypes.Transact, Name = "Main" });
        await db.SaveChangesAsync();

        return (db, user, accountNumber);
    }

    /// <summary>A stored pin record, created at <paramref name="now"/> and expiring 5 minutes later.</summary>
    private static OtpCode NewOtp(long userId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CodeHash = new string('a', 64),
        CreatedAt = now,
        ExpiresAt = now.AddMinutes(5)
    };

    /// <summary>When a user has a separate contact email, the pin is sent there rather than to their login email.</summary>
    [PostgresFact]
    public async Task FindUserByAccountNumber_PrefersContactEmail()
    {
        var (db, user, accountNumber) = await SeedAsync(withContact: true);
        await using var _ = db;
        var repository = new AuthenticationRepository(db);

        var found = await repository.FindUserByIdentifierAsync(accountNumber, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.UserId);
        Assert.StartsWith("contact-", found.Email);
    }

    /// <summary>Without a contact email, the login email is used. An unknown account number finds nobody.</summary>
    [PostgresFact]
    public async Task FindUserByAccountNumber_FallsBackToLoginEmail_AndReturnsNullWhenMissing()
    {
        var (db, user, accountNumber) = await SeedAsync(withContact: false);
        await using var _ = db;
        var repository = new AuthenticationRepository(db);

        var found = await repository.FindUserByIdentifierAsync(accountNumber, CancellationToken.None);
        var nobody = await repository.FindUserByIdentifierAsync("0000000000", CancellationToken.None);

        Assert.Equal(user.Email, found!.Email);
        Assert.Null(nobody);
    }

    /// <summary>
    /// Customers sign in with an account number; consultants sign in with a username. Never the other way round.
    /// </summary>
    [PostgresFact]
    public async Task FindUserByIdentifier_Username_ResolvesAConsultantOnly()
    {
        var (db, user, accountNumber) = await SeedAsync(withContact: true);
        await using var _ = db;
        var repository = new AuthenticationRepository(db);

        // While a Customer: the username does not sign them in, but the account number does.
        Assert.Null(await repository.FindUserByIdentifierAsync(user.Username, CancellationToken.None));
        Assert.NotNull(await repository.FindUserByIdentifierAsync(accountNumber, CancellationToken.None));

        // Promote them to Consultant.
        user.Role = UserRoles.Consultant;
        await db.SaveChangesAsync();

        // Now the username works (and uses the contact email), and the account number no longer does.
        var found = await repository.FindUserByIdentifierAsync(user.Username, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(user.Id, found.UserId);
        Assert.Equal(UserRoles.Consultant, found.Role);
        Assert.StartsWith("contact-", found.Email);
        Assert.Null(await repository.FindUserByIdentifierAsync(accountNumber, CancellationToken.None));
    }

    /// <summary>
    /// The full life of a stored pin:
    ///   - the newest pin is the one that counts,
    ///   - guesses are counted and capped at the limit,
    ///   - a pin can be used up only once,
    ///   - an expired pin accepts no guesses.
    /// </summary>
    [PostgresFact]
    public async Task OtpLifecycle_AttemptsCappedAndConsumedOnce()
    {
        var (db, user, _) = await SeedAsync(withContact: false);
        await using var __ = db;
        var repository = new AuthenticationRepository(db);
        var now = DateTime.UtcNow;

        var older = NewOtp(user.Id, now.AddMinutes(-2));
        var latest = NewOtp(user.Id, now);
        await repository.AddOtpAsync(older, CancellationToken.None);
        await repository.AddOtpAsync(latest, CancellationToken.None);

        // The newest pin is returned.
        Assert.Equal(latest.Id, (await repository.GetLatestOtpAsync(user.Id, CancellationToken.None))!.Id);

        // With a limit of 2 guesses: the first two are allowed, the third is refused.
        const int maxGuesses = 2;
        Assert.True(await repository.TryReserveAttemptAsync(latest.Id, maxGuesses, now, CancellationToken.None));
        Assert.True(await repository.TryReserveAttemptAsync(latest.Id, maxGuesses, now, CancellationToken.None));
        Assert.False(await repository.TryReserveAttemptAsync(latest.Id, maxGuesses, now, CancellationToken.None));

        // The pin can be marked as used once; a second attempt to use it fails.
        Assert.True(await repository.TryConsumeAsync(latest.Id, now, CancellationToken.None));
        Assert.False(await repository.TryConsumeAsync(latest.Id, now, CancellationToken.None));

        // 10 minutes later the older pin has expired, so no guess is allowed even though it has guesses left.
        Assert.False(await repository.TryReserveAttemptAsync(older.Id, 3, now.AddMinutes(10), CancellationToken.None));
    }
}
