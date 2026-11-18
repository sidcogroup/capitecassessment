using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.Services;

public interface IConsultantService
{
    Task<PagedResponse<ClientSummaryResponse>> GetClientsAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<ClientProfileResponse?> GetClientProfileAsync(long userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientAccountResponse>?> GetClientAccountsAsync(long userId, CancellationToken cancellationToken);

    Task<PagedResponse<TransactionResponse>?> GetAccountTransactionsAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken);

    Task<PagedResponse<DisputeResponse>> GetAllDisputesAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<PagedResponse<DisputeResponse>?> GetClientDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Updates the dispute and, when that succeeds, emails the client. A failed email does not fail the update.</summary>
    Task<UpdateDisputeResult> UpdateDisputeAsync(
        long consultantId, string disputeReference, UpdateDisputeRequest request, CancellationToken cancellationToken);
}
