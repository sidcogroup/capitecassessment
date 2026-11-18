namespace CapitecAssessment.Models.Response;

public sealed class TransactionDetailResponse
{
    public Guid Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;

    /// <summary>Always positive; the transaction type gives the direction.</summary>
    public decimal Amount { get; init; }

    public string? Reference { get; init; }
}
