using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.DataAccess;

public interface ITransactionRepository
{
    /// <summary>
    /// The transaction's reference, amount and account number, or null when it does not exist or
    /// its account does not belong to <paramref name="userId"/> (deliberately indistinguishable).
    /// </summary>
    Task<TransactionDetailResponse?> GetByIdAsync(long userId, Guid transactionId, CancellationToken cancellationToken);
}
