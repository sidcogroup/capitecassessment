using System.Security.Cryptography;
using CapitecAssessment.Abstractions.DataAccess;
using CapitecAssessment.Models;
using CapitecAssessment.Models.Response;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CapitecAssessment.DataAccess;

public class DisputeRepository : IDisputeRepository
{
    private readonly ApplicationDbContext _db;

    public DisputeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CreateDisputeResult> CreateAsync(
        long userId, Guid transactionId, string reason, string? details, CancellationToken cancellationToken)
    {
        // Ownership is part of the lookup: someone else's transaction matches nothing.
        var ownsTransaction = await _db.Transactions
            .AsNoTracking()
            .AnyAsync(t => t.Id == transactionId && t.Account!.UserId == userId, cancellationToken);
        if (!ownsTransaction)
        {
            return new CreateDisputeResult(CreateDisputeOutcome.TransactionNotFound);
        }

        var hasOpenDispute = await _db.Disputes
            .AsNoTracking()
            .AnyAsync(d => d.TransactionId == transactionId && DisputeStatuses.Open.Contains(d.Status), cancellationToken);
        if (hasOpenDispute)
        {
            return new CreateDisputeResult(CreateDisputeOutcome.OpenDisputeExists);
        }

        // Id, Status, CreatedAt and UpdatedAt come from the column defaults ("Submitted", now()).
        var dispute = new Dispute
        {
            DisputeReference = GenerateReference(),
            TransactionId = transactionId,
            Reason = reason,
            Details = details
        };
        var history = new DisputeStatusHistory
        {
            Dispute = dispute,
            Status = DisputeStatuses.Submitted,
            Note = "Dispute submitted.",
            ChangedBy = userId
        };
        _db.Disputes.Add(dispute);
        _db.DisputeStatusHistory.Add(history);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent request: ux_dispute_open_per_txn rejected the second insert.
            return new CreateDisputeResult(CreateDisputeOutcome.OpenDisputeExists);
        }

        // Re-read so the response carries the values the database generated.
        var created = await _db.Disputes
            .AsNoTracking()
            .Where(d => d.Id == dispute.Id)
            .Select(d => new DisputeResponse
            {
                Id = d.Id,
                DisputeReference = d.DisputeReference,
                TransactionId = d.TransactionId,
                Reason = d.Reason,
                Details = d.Details,
                Status = d.Status,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .SingleAsync(cancellationToken);

        return new CreateDisputeResult(CreateDisputeOutcome.Created, created);
    }

    public async Task<PagedResponse<DisputeResponse>> GetDisputesAsync(
        long userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Ownership is part of the query: only disputes on this user's accounts match.
        var owned = _db.Disputes
            .AsNoTracking()
            .Where(d => d.Transaction!.Account!.UserId == userId);

        var totalCount = await owned.CountAsync(cancellationToken);

        var items = await owned
            // Id breaks ties so rows never repeat or vanish between pages.
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
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
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<DisputeResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<IReadOnlyList<DisputeStatusHistoryResponse>?> GetStatusHistoryAsync(
        long userId, string disputeReference, CancellationToken cancellationToken)
    {
        var disputeId = await _db.Disputes
            .AsNoTracking()
            .Where(d => d.DisputeReference == disputeReference && d.Transaction!.Account!.UserId == userId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (disputeId is null)
        {
            return null;
        }

        // Served by ix_history_dispute (dispute_id, changed_at).
        return await _db.DisputeStatusHistory
            .AsNoTracking()
            .Where(h => h.DisputeId == disputeId)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new DisputeStatusHistoryResponse
            {
                Id = h.Id,
                DisputeId = h.DisputeId,
                Status = h.Status,
                Note = h.Note,
                ChangedAt = h.ChangedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>"DISP" followed by 8 random upper-case hex characters, e.g. DISP0B91CD64.</summary>
    private static string GenerateReference() =>
        "DISP" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
}
