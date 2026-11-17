using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.DataAccess;

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the account's transactions dated on or after <paramref name="fromUtc"/>, newest
    /// first. Returns null when the account does not exist or does not belong to
    /// <paramref name="userId"/> (the two cases are deliberately indistinguishable); an account
    /// with no matching transactions returns an empty page.
    /// </summary>
    Task<PagedResponse<TransactionResponse>?> GetTransactionsAsync(
        long userId, string accountNumber, DateTime fromUtc, int page, int pageSize, CancellationToken cancellationToken);
}
