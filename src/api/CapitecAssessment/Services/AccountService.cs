using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _time;

    public AccountService(IAccountRepository accounts, TimeProvider time)
    {
        _accounts = accounts;
        _time = time;
    }

    public Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(long userId, CancellationToken cancellationToken) =>
        _accounts.GetAccountsAsync(userId, cancellationToken);

    public Task<PagedResponse<TransactionResponse>?> GetTransactionsAsync(
        long userId, string accountNumber, int days, int page, int pageSize, CancellationToken cancellationToken)
    {
        var fromUtc = _time.GetUtcNow().UtcDateTime.AddDays(-days);

        // The repository only matches accounts owned by userId, so this is the ownership check.
        return _accounts.GetTransactionsAsync(userId, accountNumber, fromUtc, page, pageSize, cancellationToken);
    }
}
