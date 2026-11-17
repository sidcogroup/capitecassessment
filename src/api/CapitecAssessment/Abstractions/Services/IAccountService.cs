using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface IAccountService
{
    Task<IReadOnlyList<AccountResponse>> GetAccountsAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the account's transactions from the last <paramref name="days"/> days, newest
    /// first, or null when <paramref name="accountNumber"/> is not one of <paramref name="userId"/>'s
    /// accounts (including when it does not exist at all).
    /// </summary>
    Task<PagedResponse<TransactionResponse>?> GetTransactionsAsync(
        long userId, string accountNumber, int days, int page, int pageSize, CancellationToken cancellationToken);
}
