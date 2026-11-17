using CapitecAssessment.DataAccess;
using CapitecAssessment.Models;

namespace CapitecAssessment.Tests.DataAccess;

/// <summary>
/// The ownership filter and ordering only mean something against real PostgreSQL. Each test runs in
/// a transaction that is rolled back. Skipped unless CAPITEC_TEST_CONNECTION is set.
/// </summary>
public class AccountRepositoryTests
{
    private static string NewAccountNumber() => Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString();

    private static async Task<User> AddUserAsync(ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new User { Email = $"{suffix}@example.test", Username = $"u{suffix}" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [PostgresFact]
    public async Task Accounts_AreScopedToTheUser_AndTransactionsToTheirOwner()
    {
        await using var db = PostgresTestDb.NewContext();
        await db.Database.BeginTransactionAsync();

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

        var accounts = await repository.GetAccountsAsync(owner.Id, CancellationToken.None);
        var account = Assert.Single(accounts);
        Assert.Equal(mine.AccountNumber, account.AccountNumber);
        Assert.Equal("Main", account.AccountName);
        Assert.Equal(120.50m, account.Balance);

        var from = now.AddDays(-30);
        var own = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, from, 1, 20, CancellationToken.None);
        Assert.Equal(new[] { "NEW", "OLD" }, own!.Items.Select(t => t.Description));
        Assert.Equal(2, own.TotalCount);

        // Paging and the date window are applied in the database.
        var secondPage = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, from, 2, 1, CancellationToken.None);
        Assert.Equal("OLD", Assert.Single(secondPage!.Items).Description);
        Assert.Equal(2, secondPage.TotalCount);

        var narrow = await repository.GetTransactionsAsync(owner.Id, mine.AccountNumber, now.AddDays(-1.5), 1, 20, CancellationToken.None);
        Assert.Equal("NEW", Assert.Single(narrow!.Items).Description);

        Assert.Null(await repository.GetTransactionsAsync(owner.Id, theirs.AccountNumber, from, 1, 20, CancellationToken.None));
        Assert.Null(await repository.GetTransactionsAsync(owner.Id, "0000000000", from, 1, 20, CancellationToken.None));
    }
}
