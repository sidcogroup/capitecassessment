using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

public class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _db;

    public AccountRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(long userId, CancellationToken cancellationToken)
    {
        // Served by ix_accounts_user.
        return await _db.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new AccountResponse
            {
                AccountName = a.Name,
                AccountNumber = a.AccountNumber,
                AccountType = a.AccountType,
                Balance = a.Balance,
                Currency = a.Currency
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<TransactionResponse>?> GetTransactionsAsync(
        long userId, string accountNumber, DateTime fromUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Ownership is part of the lookup itself: someone else's account number matches nothing.
        var accountId = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.AccountNumber == accountNumber && a.UserId == userId)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is null)
        {
            return null;
        }

        // Served by ix_txn_account_date (account_id, transaction_date DESC).
        var inRange = _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId && t.TransactionDate >= fromUtc);

        var totalCount = await inRange.CountAsync(cancellationToken);

        var items = await inRange
            // Id breaks ties so rows never repeat or vanish between pages.
            .OrderByDescending(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionResponse
            {
                Id = t.Id,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                Merchant = t.Merchant,
                Amount = t.Amount,
                Type = t.Type,
                Reference = t.Reference
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<TransactionResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }
}
