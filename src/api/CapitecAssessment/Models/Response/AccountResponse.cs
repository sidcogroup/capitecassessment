namespace CapitecAssessment.Models.Response;

public sealed class AccountResponse
{
    public string AccountName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;

    /// <summary>See <see cref="AccountTypes"/>.</summary>
    public string AccountType { get; init; } = string.Empty;

    /// <summary>Credit-card balances are negative (the amount owed).</summary>
    public decimal Balance { get; init; }

    public string Currency { get; init; } = string.Empty;
}
