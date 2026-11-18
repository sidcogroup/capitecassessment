using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Services;

public class ConsultantService : IConsultantService
{
    private readonly IConsultantRepository _consultants;
    private readonly IMessagingService _messaging;
    private readonly ILogger<ConsultantService> _logger;

    public ConsultantService(IConsultantRepository consultants, IMessagingService messaging, ILogger<ConsultantService> logger)
    {
        _consultants = consultants;
        _messaging = messaging;
        _logger = logger;
    }

    public Task<PagedResponse<ClientSummaryResponse>> GetClientsAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        _consultants.GetClientsAsync(page, pageSize, cancellationToken);

    public Task<ClientProfileResponse?> GetClientProfileAsync(long userId, CancellationToken cancellationToken) =>
        _consultants.GetClientProfileAsync(userId, cancellationToken);

    public Task<IReadOnlyList<ClientAccountResponse>?> GetClientAccountsAsync(long userId, CancellationToken cancellationToken) =>
        _consultants.GetClientAccountsAsync(userId, cancellationToken);

    public Task<PagedResponse<TransactionResponse>?> GetAccountTransactionsAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        _consultants.GetAccountTransactionsAsync(accountId, page, pageSize, cancellationToken);

    public Task<PagedResponse<DisputeResponse>> GetAllDisputesAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        _consultants.GetAllDisputesAsync(page, pageSize, cancellationToken);

    public Task<PagedResponse<DisputeResponse>?> GetClientDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken) =>
        _consultants.GetClientDisputesAsync(userId, page, pageSize, cancellationToken);

    public async Task<UpdateDisputeResult> UpdateDisputeAsync(
        long consultantId, string disputeReference, UpdateDisputeRequest request, CancellationToken cancellationToken)
    {
        var note = request.Note.Trim();
        var result = await _consultants.UpdateDisputeStatusAsync(
            consultantId, disputeReference, request.Status, note, cancellationToken);

        if (result.Outcome == UpdateDisputeOutcome.Updated && result.ClientEmail is not null)
        {
            try
            {
                await _messaging.SendDisputeUpdateAsync(
                    result.ClientUserId, result.ClientEmail, result.Dispute!.Id, result.Dispute.DisputeReference,
                    request.Status, note, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The update is already committed; the failure is recorded as a Failed notification.
                _logger.LogError(ex, "Dispute {DisputeReference} was updated but the client email failed", disputeReference);
            }
        }

        return result;
    }
}
