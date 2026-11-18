namespace CapitecAssessment.Models.Response;

/// <summary>An account as seen by a consultant. Unlike <see cref="AccountResponse"/> it carries the id used to list transactions.</summary>
public sealed class ClientAccountResponse
{
    public Guid AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;

    /// <summary>Credit-card balances are negative (the amount owed).</summary>
    public decimal Balance { get; init; }

    public string Currency { get; init; } = string.Empty;
}
