using System.Text.Json.Serialization;

namespace CapitecAssessment.Models.Response;

public sealed class DisputeResponse
{
    // Internal only; not part of the API payload.
    [JsonIgnore]
    public Guid Id { get; init; }

    public Guid TransactionId { get; init; }

    /// <summary>Customer-facing reference, e.g. DISP0B91CD64.</summary>
    public string DisputeReference { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
    public string? Details { get; init; }

    /// <summary>See <see cref="DisputeStatuses"/>.</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Filled in by DisputeService from the transaction, not by the dispute query.
    public string? TransactionReference { get; set; }
    public decimal TransactionAmount { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
}
