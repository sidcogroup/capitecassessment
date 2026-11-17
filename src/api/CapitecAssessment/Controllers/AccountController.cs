using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using CapitecAssessment.Abstractions.Services;
using CapitecAssessment.Models.Request;
using CapitecAssessment.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CapitecAssessment.Controllers;

/// <summary>
/// The signed-in user's accounts and transactions. The user is always taken from the session
/// cookie, never from the request, so one customer can never read another's data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounts")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    /// <summary>All accounts that belong to the signed-in user.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AccountResponse>>> GetAccounts(CancellationToken cancellationToken)
    {
        var accounts = await _accountService.GetAccountsAsync(CurrentUserId(), cancellationToken);
        return Ok(accounts);
    }

    /// <summary>One page of transactions on one of the signed-in user's accounts, newest first.</summary>
    /// <remarks>
    /// Query: <c>days</c> = 30 (default), 60 or 90; <c>page</c> = 1-based (default 1);
    /// <c>pageSize</c> = 1-100 (default 20).
    /// Returns 404 both when the account does not exist and when it belongs to someone else,
    /// so account numbers cannot be probed.
    /// </remarks>
    [HttpGet("{accountNumber}/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<TransactionResponse>>> GetTransactions(
        [RegularExpression("^[0-9]{10}$", ErrorMessage = "Account number must be exactly 10 digits.")] string accountNumber,
        [FromQuery] TransactionsQueryRequest query,
        CancellationToken cancellationToken)
    {
        var transactions = await _accountService.GetTransactionsAsync(
            CurrentUserId(), accountNumber, query.Days, query.Page, query.PageSize, cancellationToken);
        if (transactions is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: AccountNotFoundTitle);
        }

        return Ok(transactions);
    }

    public const string AccountNotFoundTitle = "Account not found.";

    private long CurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}
