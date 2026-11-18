using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface IDisputeService
{
    Task<CreateDisputeResult> CreateDisputeAsync(long userId, CreateDisputeRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<DisputeResponse>> GetDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Null when the dispute is not found for this user.</summary>
    Task<IReadOnlyList<DisputeStatusHistoryResponse>?> GetStatusHistoryAsync(
        long userId, string disputeReference, CancellationToken cancellationToken);
}
