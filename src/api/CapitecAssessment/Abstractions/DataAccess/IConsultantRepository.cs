using CapitecAssessment.Models.Response;

namespace CapitecAssessment.Abstractions.DataAccess;

public enum UpdateDisputeOutcome
{
    Updated,
    NotFound,

    /// <summary>The requested status is not reachable from the dispute's current status.</summary>
    InvalidTransition
}

/// <param name="ClientUserId">The dispute owner, for the notification.</param>
/// <param name="ClientEmail">The dispute owner's login email.</param>
public sealed record UpdateDisputeResult(
    UpdateDisputeOutcome Outcome,
    DisputeResponse? Dispute = null,
    long ClientUserId = 0,
    string? ClientEmail = null);

/// <summary>Consultant queries. Unlike the customer repositories, nothing here is scoped to the signed-in user.</summary>
public interface IConsultantRepository
{
    /// <summary>One page of users with the Customer role, ordered by name.</summary>
    Task<PagedResponse<ClientSummaryResponse>> GetClientsAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The client's profile, or null when no Customer-role user has that id.</summary>
    Task<ClientProfileResponse?> GetClientProfileAsync(long userId, CancellationToken cancellationToken);

    /// <summary>The client's accounts, or null when no Customer-role user has that id.</summary>
    Task<IReadOnlyList<ClientAccountResponse>?> GetClientAccountsAsync(long userId, CancellationToken cancellationToken);

    /// <summary>One page of the account's transactions, newest first, or null when the account does not exist.</summary>
    Task<PagedResponse<TransactionResponse>?> GetAccountTransactionsAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of every dispute, newest first.</summary>
    Task<PagedResponse<DisputeResponse>> GetAllDisputesAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the client's disputes, newest first, or null when no Customer-role user has that id.</summary>
    Task<PagedResponse<DisputeResponse>?> GetClientDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the dispute to <paramref name="newStatus"/> if the transition is allowed, and writes the
    /// status-history row (with the consultant as changed_by) in the same transaction.
    /// </summary>
    Task<UpdateDisputeResult> UpdateDisputeStatusAsync(
        long consultantId, string disputeReference, string newStatus, string note, CancellationToken cancellationToken);
}
