namespace CapitecAssessment.Models.Response;

public sealed class TransactionResponse
{
    public Guid Id { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Merchant { get; init; }

    /// <summary>Always positive; <see cref="Type"/> gives the direction.</summary>
    public decimal Amount { get; init; }

    /// <summary>See <see cref="TransactionTypes"/>.</summary>
    public string Type { get; init; } = string.Empty;

    public string? Reference { get; init; }
}
