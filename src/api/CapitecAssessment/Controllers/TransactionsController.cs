using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CapitecAssessment.Controllers;

/// <summary>
/// Disputes on the signed-in user's transactions. The user is always taken from the session
/// cookie, never from the request.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    public const string TransactionNotFoundTitle = "Transaction not found.";
    public const string DisputeNotFoundTitle = "Dispute not found.";
    public const string OpenDisputeExistsTitle = "This transaction already has an open dispute.";

    private readonly IDisputeService _disputeService;

    public TransactionsController(IDisputeService disputeService)
    {
        _disputeService = disputeService;
    }

    /// <summary>Raise a dispute on one of the signed-in user's transactions.</summary>
    /// <remarks>
    /// The dispute starts with status "Submitted". Returns 404 when the transaction does not exist
    /// or belongs to someone else, and 409 when it already has a Submitted / Under Review dispute.
    /// </remarks>
    [HttpPost("dispute")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeResponse>> Dispute(
        [FromBody] CreateDisputeRequest request, CancellationToken cancellationToken)
    {
        var result = await _disputeService.CreateDisputeAsync(CurrentUserId(), request, cancellationToken);

        return result.Outcome switch
        {
            CreateDisputeOutcome.Created => CreatedAtAction(
                nameof(GetDisputeStatusHistory),
                new { version = "1.0", disputeReference = result.Dispute!.DisputeReference },
                result.Dispute),
            CreateDisputeOutcome.OpenDisputeExists =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: OpenDisputeExistsTitle),
            _ => Problem(statusCode: StatusCodes.Status404NotFound, title: TransactionNotFoundTitle)
        };
    }

    /// <summary>One page of all the signed-in user's disputes, with their statuses, newest first.</summary>
    /// <remarks>
    /// Route: <c>GET /api/v1/disputes</c>. Query: <c>page</c> = 1-based (default 1);
    /// <c>pageSize</c> = 1-50 (default 10).
    /// </remarks>
    [HttpGet("~/api/v{version:apiVersion}/disputes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResponse<DisputeResponse>>> GetDisputes(
        [FromQuery] DisputesQueryRequest query, CancellationToken cancellationToken)
    {
        var disputes = await _disputeService.GetDisputesAsync(
            CurrentUserId(), query.Page, query.PageSize, cancellationToken);

        return Ok(disputes);
    }

    /// <summary>Status history of one of the signed-in user's disputes, oldest first.</summary>
    /// <remarks>Returns 404 both when the dispute does not exist and when it belongs to someone else.</remarks>
    [HttpGet("dispute/{disputeReference}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DisputeStatusHistoryResponse>>> GetDisputeStatusHistory(
        string disputeReference, CancellationToken cancellationToken)
    {
        var history = await _disputeService.GetStatusHistoryAsync(CurrentUserId(), disputeReference, cancellationToken);
        if (history is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: DisputeNotFoundTitle);
        }

        return Ok(history);
    }

    private long CurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
