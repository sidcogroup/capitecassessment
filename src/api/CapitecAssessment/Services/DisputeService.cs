using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class DisputeService : IDisputeService
{
    private readonly IDisputeRepository _disputes;
    private readonly ITransactionService _transactions;

    public DisputeService(IDisputeRepository disputes, ITransactionService transactions)
    {
        _disputes = disputes;
        _transactions = transactions;
    }

    public async Task<CreateDisputeResult> CreateDisputeAsync(
        long userId, CreateDisputeRequest request, CancellationToken cancellationToken)
    {
        var result = await _disputes.CreateAsync(
            userId, request.TransactionId, request.Reason.Trim(), request.Details?.Trim(), cancellationToken);

        if (result.Dispute is not null)
        {
            await EnrichAsync(userId, result.Dispute, cancellationToken);
        }

        return result;
    }

    public async Task<PagedResponse<DisputeResponse>> GetDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var disputes = await _disputes.GetDisputesAsync(userId, page, pageSize, cancellationToken);

        // One lookup per dispute: a DbContext is not thread-safe, so these run sequentially.
        foreach (var dispute in disputes.Items)
        {
            await EnrichAsync(userId, dispute, cancellationToken);
        }

        return disputes;
    }

    public Task<IReadOnlyList<DisputeStatusHistoryResponse>?> GetStatusHistoryAsync(
        long userId, string disputeReference, CancellationToken cancellationToken) =>
        _disputes.GetStatusHistoryAsync(userId, disputeReference, cancellationToken);

    private async Task EnrichAsync(long userId, DisputeResponse dispute, CancellationToken cancellationToken)
    {
        var transaction = await _transactions.GetTransactionAsync(userId, dispute.TransactionId, cancellationToken);
        if (transaction is null)
        {
            return;
        }

        dispute.TransactionReference = transaction.Reference;
        dispute.TransactionAmount = transaction.Amount;
        dispute.AccountNumber = transaction.AccountNumber;
    }
}
