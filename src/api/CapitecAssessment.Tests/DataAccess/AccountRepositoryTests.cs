using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="AccountRepository"/> against a REAL PostgreSQL database.
///
/// Why a real database: the "only show the owner's accounts" filter, the sorting and the paging
/// all happen inside SQL, so an in-memory fake could not prove they work.
///
/// Each test runs inside a database transaction that is never committed, so nothing is left behind.
/// Skipped unless the CAPITEC_TEST_CONNECTION environment variable is set (see <see cref="PostgresDbTests"/>).
/// </summary>
public class AccountRepositoryTests
{
    /// <summary>A random 10-digit account number, so test runs never clash with existing data.</summary>
    private static string NewAccountNumber() => Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString();

    /// <summary>Saves a new user with a unique email and username.</summary>
    private static async Task<User> AddUserAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new User { Email = $"{suffix}@example.test", Username = $"u{suffix}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// A user sees only their own account, and only their own account's transactions, newest first.
    /// Asking for someone else's account (or one that does not exist) returns null, i.e. "not found".
    /// </summary>
    [PostgresFact]
    public async Task Accounts_AreScopedToTheUser_AndTransactionsToTheirOwner()
    {
        // Arrange: two users, one account each. "mine" has an OLD and a NEW transaction; "theirs" has a SECRET one.
        await using var db = PostgresTestDb.NewContext();
        await db.Database.BeginTransactionAsync(); // rolled back automatically when the test ends

        var owner = await AddUserAsync(db);
        var stranger = await AddUserAsync(db);
        var mine = new Account { UserId = owner.Id, AccountNumber = NewAccountNumber(), AccountType = AccountTypes.Transact, Name = "Main", Balance = 120.50m };
        var theirs = new Account { UserId = stranger.Id, AccountNumber = NewAccountNumber(), AccountType = AccountTypes.Savings, Name = "Theirs" };
        db.Accounts.AddRange(mine, theirs);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.Transactions.AddRange(
            new Transaction { AccountId = mine.Id, TransactionDate = now.AddDays(-2), Description = "OLD", Amount = 1m, Type = TransactionTypes.Debit },
            new Transaction { AccountId = mine.Id, TransactionDate = now.AddDays(-1), Description = "NEW", Amount = 2m, Type = TransactionTypes.Credit },
            new Transaction { AccountId = theirs.Id, TransactionDate = now, Description = "SECRET", Amount = 3m, Type = TransactionTypes.Debit });
        await db.SaveChangesAsync();

        var repository = new AccountRepository(db);

        // The owner's account list contains only their own account, with the right details.
        var accounts = await repository.GetAccountsAsync(owner.Id, CancellationToken.None);
        var account = Assert.Single(accounts);
        Assert.Equal(mine.AccountNumber, account.AccountNumber);
        Assert.Equal("Main", account.AccountName);
        Assert.Equal(120.50m, account.Balance);

        // Their transactions from the last 30 days, newest first (arguments: page 1, 20 per page).
        var thirtyDaysAgo = now.AddDays(-30);
        var own = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, thirtyDaysAgo, 1, 20, CancellationToken.None);
        Assert.Equal(new[] { "NEW", "OLD" }, own!.Items.Select(t => t.Description));
        Assert.Equal(2, own.TotalCount);

        // Paging works in the database: page 2 with 1 per page is the older transaction.
        var secondPage = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, thirtyDaysAgo, 2, 1, CancellationToken.None);
        Assert.Equal("OLD", Assert.Single(secondPage!.Items).Description);
        Assert.Equal(2, secondPage.TotalCount);

        // The date window works in the database: looking back 1.5 days excludes the 2-day-old transaction.
        var narrow = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, now.AddDays(-1.5), 1, 20, CancellationToken.None);
        Assert.Equal("NEW", Assert.Single(narrow!.Items).Description);

        // Someone else's account, and an account that does not exist, both return null ("not found").
        Assert.Null(await repository.GetTransactionsAsync(owner.Id, theirs.AccountNumber, thirtyDaysAgo, 1, 20, CancellationToken.None));
        Assert.Null(await repository.GetTransactionsAsync(owner.Id, "0000000000", thirtyDaysAgo, 1, 20, CancellationToken.None));
    }
}
