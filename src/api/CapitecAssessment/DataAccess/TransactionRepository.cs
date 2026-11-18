using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

public class TransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext _db;

    public TransactionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<TransactionDetailResponse?> GetByIdAsync(long userId, Guid transactionId, CancellationToken cancellationToken) =>
        // Primary-key lookup; ownership is part of the query, so someone else's transaction matches nothing.
        _db.Transactions
            .AsNoTracking()
            .Where(t => t.Id == transactionId && t.Account!.UserId == userId)
            .Select(t => new TransactionDetailResponse
            {
                Id = t.Id,
                AccountNumber = t.Account!.AccountNumber,
                Amount = t.Amount,
                Reference = t.Reference
            })
            .SingleOrDefaultAsync(cancellationToken);
}
