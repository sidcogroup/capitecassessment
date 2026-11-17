using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// The conditional UPDATEs only mean something against real PostgreSQL. Each test runs in a
/// transaction that is rolled back. Skipped unless CAPITEC_TEST_CONNECTION is set.
/// </summary>
public class AuthenticationRepositoryTests
{
    private static async Task<(ApplicationDbContext Db, User User, string AccountNumber)> SeedAsync(bool withContact)
    {
        var db = PostgresTestDb.NewContext();
        await db.Database.BeginTransactionAsync();

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

    private static OtpCode NewOtp(long userId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CodeHash = new string('a', 64),
        CreatedAt = now,
        ExpiresAt = now.AddMinutes(5)
    };

    [PostgresFact]
    public async Task FindUserByAccountNumber_PrefersContactEmail()
    {
        var (db, user, accountNumber) = await SeedAsync(withContact: true);
        await using var _ = db;
        var repository = new AuthenticationRepository(db);

        var found = await repository.FindUserByAccountNumberAsync(accountNumber, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.UserId);
        Assert.StartsWith("contact-", found.Email);
    }

    [PostgresFact]
    public async Task FindUserByAccountNumber_FallsBackToLoginEmail_AndReturnsNullWhenMissing()
    {
        var (db, user, accountNumber) = await SeedAsync(withContact: false);
        await using var _ = db;
        var repository = new AuthenticationRepository(db);

        Assert.Equal(user.Email, (await repository.FindUserByAccountNumberAsync(accountNumber, CancellationToken.None))!.Email);
        Assert.Null(await repository.FindUserByAccountNumberAsync("0000000000", CancellationToken.None));
    }

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

        Assert.Equal(latest.Id, (await repository.GetLatestOtpAsync(user.Id, CancellationToken.None))!.Id);

        Assert.True(await repository.TryReserveAttemptAsync(latest.Id, 2, now, CancellationToken.None));
        Assert.True(await repository.TryReserveAttemptAsync(latest.Id, 2, now, CancellationToken.None));
        Assert.False(await repository.TryReserveAttemptAsync(latest.Id, 2, now, CancellationToken.None));

        Assert.True(await repository.TryConsumeAsync(latest.Id, now, CancellationToken.None));
        Assert.False(await repository.TryConsumeAsync(latest.Id, now, CancellationToken.None));

        // Expired codes cannot be attempted.
        Assert.False(await repository.TryReserveAttemptAsync(older.Id, 3, now.AddMinutes(10), CancellationToken.None));
    }
}
