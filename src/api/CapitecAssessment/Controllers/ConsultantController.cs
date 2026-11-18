using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CapitecAssessment.Controllers;

/// <summary>
/// Back-office endpoints for consultants. Uses the same session cookie as customers; the role claim
/// lifts the per-user scoping, so a consultant can see every Customer's data. Customers get 403.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/consultant")]
[Authorize(Roles = UserRoles.Consultant)]
public class ConsultantController : ControllerBase
{
    public const string ClientNotFoundTitle = "Client not found.";
    public const string AccountNotFoundTitle = "Account not found.";
    public const string DisputeNotFoundTitle = "Dispute not found.";
    public const string InvalidTransitionTitle = "The dispute cannot be moved to that status from its current status.";

    private readonly IConsultantService _consultantService;

    public ConsultantController(IConsultantService consultantService)
    {
        _consultantService = consultantService;
    }

    /// <summary>One page of all clients (users with the Customer role).</summary>
    [HttpGet("clients")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<ClientSummaryResponse>>> GetClients(
        [FromQuery] PagingQueryRequest query, CancellationToken cancellationToken) =>
        Ok(await _consultantService.GetClientsAsync(query.Page, query.PageSize, cancellationToken));

    /// <summary>A client's profile and contact details.</summary>
    [HttpGet("clients/{userId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientProfileResponse>> GetClientProfile(
        long userId, CancellationToken cancellationToken)
    {
        var profile = await _consultantService.GetClientProfileAsync(userId, cancellationToken);
        return profile is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: ClientNotFoundTitle)
            : Ok(profile);
    }

    /// <summary>All accounts linked to a client.</summary>
    [HttpGet("clients/{userId:long}/accounts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ClientAccountResponse>>> GetClientAccounts(
        long userId, CancellationToken cancellationToken)
    {
        var accounts = await _consultantService.GetClientAccountsAsync(userId, cancellationToken);
        return accounts is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: ClientNotFoundTitle)
            : Ok(accounts);
    }

    /// <summary>One page of the transactions on an account, newest first.</summary>
    [HttpGet("accounts/{accountId:guid}/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<TransactionResponse>>> GetAccountTransactions(
        Guid accountId, [FromQuery] PagingQueryRequest query, CancellationToken cancellationToken)
    {
        var transactions = await _consultantService.GetAccountTransactionsAsync(
            accountId, query.Page, query.PageSize, cancellationToken);
        return transactions is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: AccountNotFoundTitle)
            : Ok(transactions);
    }

    /// <summary>One page of all disputes across every client, newest first.</summary>
    [HttpGet("disputes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<DisputeResponse>>> GetAllDisputes(
        [FromQuery] PagingQueryRequest query, CancellationToken cancellationToken) =>
        Ok(await _consultantService.GetAllDisputesAsync(query.Page, query.PageSize, cancellationToken));

    /// <summary>One page of the disputes raised by a client, newest first.</summary>
    [HttpGet("clients/{userId:long}/disputes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<DisputeResponse>>> GetClientDisputes(
        long userId, [FromQuery] PagingQueryRequest query, CancellationToken cancellationToken)
    {
        var disputes = await _consultantService.GetClientDisputesAsync(
            userId, query.Page, query.PageSize, cancellationToken);
        return disputes is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: ClientNotFoundTitle)
            : Ok(disputes);
    }

    /// <summary>Moves a dispute to a new status with a note, records it in the history and emails the client.</summary>
    /// <remarks>
    /// Allowed: Submitted to Under Review, Resolved or Rejected; Under Review to Resolved or Rejected.
    /// Returns 409 for any other change. The client email is best-effort and never fails the update.
    /// </remarks>
    [HttpPut("disputes/{disputeReference}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeResponse>> UpdateDispute(
        string disputeReference, [FromBody] UpdateDisputeRequest request, CancellationToken cancellationToken)
    {
        var result = await _consultantService.UpdateDisputeAsync(
            CurrentUserId(), disputeReference, request, cancellationToken);

        return result.Outcome switch
        {
            UpdateDisputeOutcome.Updated => Ok(result.Dispute),
            UpdateDisputeOutcome.InvalidTransition =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: InvalidTransitionTitle),
            _ => Problem(statusCode: StatusCodes.Status404NotFound, title: DisputeNotFoundTitle)
        };
    }

    private long CurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
