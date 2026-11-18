using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace CapitecAssessment.DataAccess;

public class ConsultantRepository : IConsultantRepository
{
    private readonly ApplicationDbContext _db;
    private readonly TimeProvider _time;

    public ConsultantRepository(ApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<PagedResponse<ClientSummaryResponse>> GetClientsAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var customers = _db.Users.AsNoTracking().Where(u => u.Role == UserRoles.Customer);

        var totalCount = await customers.CountAsync(cancellationToken);

        var items = await customers
            .OrderBy(u => u.Profile!.LastName)
            .ThenBy(u => u.Profile!.FirstName)
            .ThenBy(u => u.Id) // tie-breaker so rows never repeat or vanish between pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new ClientSummaryResponse
            {
                UserId = u.Id,
                Username = u.Username,
                FirstName = u.Profile != null ? u.Profile.FirstName : string.Empty,
                LastName = u.Profile != null ? u.Profile.LastName : string.Empty,
                Email = u.Email
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<ClientSummaryResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public Task<ClientProfileResponse?> GetClientProfileAsync(long userId, CancellationToken cancellationToken) =>
        _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.Role == UserRoles.Customer)
            .Select(u => new ClientProfileResponse
            {
                UserId = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.Profile != null ? u.Profile.FirstName : string.Empty,
                LastName = u.Profile != null ? u.Profile.LastName : string.Empty,
                ContactNumber = u.Contact != null ? u.Contact.ContactNumber : null,
                ContactEmail = u.Contact != null ? u.Contact.ContactEmail : null,
                CreatedAt = u.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ClientAccountResponse>?> GetClientAccountsAsync(
        long userId, CancellationToken cancellationToken)
    {
        var isClient = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Role == UserRoles.Customer, cancellationToken);
        if (!isClient)
        {
            return null;
        }

        return await _db.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountNumber)
            .Select(a => new ClientAccountResponse
            {
                AccountId = a.Id,
                AccountName = a.Name,
                AccountNumber = a.AccountNumber,
                AccountType = a.AccountType,
                Balance = a.Balance,
                Currency = a.Currency
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<TransactionResponse>?> GetAccountTransactionsAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var transactions = _db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId);

        var totalCount = await transactions.CountAsync(cancellationToken);

        var items = await transactions
            .OrderByDescending(t => t.TransactionDate)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionResponse
            {
                Id = t.Id,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                Merchant = t.Merchant,
                Amount = t.Amount,
                Type = t.Type,
                Reference = t.Reference
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<TransactionResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public Task<PagedResponse<DisputeResponse>> GetAllDisputesAsync(
        int page, int pageSize, CancellationToken cancellationToken) =>
        PageDisputesAsync(_db.Disputes.AsNoTracking(), page, pageSize, cancellationToken);

    public async Task<PagedResponse<DisputeResponse>?> GetClientDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var isClient = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Role == UserRoles.Customer, cancellationToken);
        if (!isClient)
        {
            return null;
        }

        return await PageDisputesAsync(
            _db.Disputes.AsNoTracking().Where(d => d.Transaction!.Account!.UserId == userId),
            page, pageSize, cancellationToken);
    }

    private static async Task<PagedResponse<DisputeResponse>> PageDisputesAsync(
        IQueryable<Dispute> disputes, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await disputes.CountAsync(cancellationToken);

        var items = await disputes
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id) // tie-breaker so rows never repeat or vanish between pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DisputeResponse
            {
                Id = d.Id,
                DisputeReference = d.DisputeReference,
                TransactionId = d.TransactionId,
                Reason = d.Reason,
                Details = d.Details,
                Status = d.Status,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                TransactionReference = d.Transaction!.Reference,
                TransactionAmount = d.Transaction!.Amount,
                AccountNumber = d.Transaction!.Account!.AccountNumber
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<DisputeResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<UpdateDisputeResult> UpdateDisputeStatusAsync(
        long consultantId, string disputeReference, string newStatus, string note, CancellationToken cancellationToken)
    {
        var current = await _db.Disputes
            .AsNoTracking()
            .Where(d => d.DisputeReference == disputeReference)
            .Select(d => new
            {
                d.Id,
                d.Status,
                ClientUserId = d.Transaction!.Account!.UserId,
                ClientEmail = d.Transaction!.Account!.User!.Email
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (current is null)
        {
            return new UpdateDisputeResult(UpdateDisputeOutcome.NotFound);
        }

        if (!IsAllowedTransition(current.Status, newStatus))
        {
            return new UpdateDisputeResult(UpdateDisputeOutcome.InvalidTransition);
        }

        // The retrying execution strategy (see AddDataAccess) requires the transaction to run inside it.
        var updated = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Conditional on the status we validated against: if another consultant got there first, 0 rows change.
            var now = _time.GetUtcNow().UtcDateTime;
            var rows = await _db.Disputes
                .Where(d => d.Id == current.Id && d.Status == current.Status)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, newStatus)
                    .SetProperty(d => d.UpdatedAt, now), cancellationToken);
            if (rows == 0)
            {
                return false;
            }

            _db.DisputeStatusHistory.Add(new DisputeStatusHistory
            {
                DisputeId = current.Id,
                Status = newStatus,
                Note = note,
                ChangedBy = consultantId
            });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

        if (!updated)
        {
            return new UpdateDisputeResult(UpdateDisputeOutcome.InvalidTransition);
        }

        var dispute = await _db.Disputes
            .AsNoTracking()
            .Where(d => d.Id == current.Id)
            .Select(d => new DisputeResponse
            {
                Id = d.Id,
                DisputeReference = d.DisputeReference,
                TransactionId = d.TransactionId,
                Reason = d.Reason,
                Details = d.Details,
                Status = d.Status,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                TransactionReference = d.Transaction!.Reference,
                TransactionAmount = d.Transaction!.Amount,
                AccountNumber = d.Transaction!.Account!.AccountNumber
            })
            .SingleAsync(cancellationToken);

        return new UpdateDisputeResult(UpdateDisputeOutcome.Updated, dispute, current.ClientUserId, current.ClientEmail);
    }

    /// <summary>Submitted -> Under Review -> Resolved / Rejected (Submitted may also close directly). Resolved and Rejected are final.</summary>
    public static bool IsAllowedTransition(string from, string to) => (from, to) switch
    {
        (DisputeStatuses.Submitted, DisputeStatuses.UnderReview) => true,
        (DisputeStatuses.Submitted or DisputeStatuses.UnderReview, DisputeStatuses.Resolved or DisputeStatuses.Rejected) => true,
        _ => false
    };
}
