using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface ITransactionService
{
    /// <summary>
    /// One transaction's details, or null when <paramref name="transactionId"/> is not a
    /// transaction on one of <paramref name="userId"/>'s accounts (including when it does not exist).
    /// </summary>
    Task<TransactionDetailResponse?> GetTransactionAsync(long userId, Guid transactionId, CancellationToken cancellationToken);
}
