using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.DataAccess;

public enum CreateDisputeOutcome
{
    Created,

    /// <summary>The transaction does not exist or is not on one of the user's accounts.</summary>
    TransactionNotFound,

    /// <summary>The transaction already has a Submitted / Under Review dispute.</summary>
    OpenDisputeExists
}

public sealed record CreateDisputeResult(CreateDisputeOutcome Outcome, DisputeResponse? Dispute = null);

public interface IDisputeRepository
{
    /// <summary>
    /// Inserts a dispute with the default "Submitted" status plus its first status-history row,
    /// in one save. Ownership of the transaction is part of the lookup.
    /// </summary>
    Task<CreateDisputeResult> CreateAsync(
        long userId, Guid transactionId, string reason, string? details, CancellationToken cancellationToken);

    /// <summary>One page of all the user's disputes (any status), most recently raised first.</summary>
    Task<PagedResponse<DisputeResponse>> GetDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Status history of a dispute, oldest first. Returns null when the dispute does not exist or
    /// is not on a transaction of one of the user's accounts.
    /// </summary>
    Task<IReadOnlyList<DisputeStatusHistoryResponse>?> GetStatusHistoryAsync(
        long userId, string disputeReference, CancellationToken cancellationToken);
}
