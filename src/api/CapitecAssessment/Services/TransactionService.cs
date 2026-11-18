using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _transactions;

    public TransactionService(ITransactionRepository transactions)
    {
        _transactions = transactions;
    }

    public Task<TransactionDetailResponse?> GetTransactionAsync(
        long userId, Guid transactionId, CancellationToken cancellationToken) =>
        _transactions.GetByIdAsync(userId, transactionId, cancellationToken);
}
