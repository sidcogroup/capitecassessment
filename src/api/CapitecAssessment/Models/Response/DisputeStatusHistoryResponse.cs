namespace CapitecAssessment.Models.Response;

public sealed class DisputeStatusHistoryResponse
{
    public Guid Id { get; init; }
    public Guid DisputeId { get; init; }

    /// <summary>See <see cref="DisputeStatuses"/>.</summary>
    public string Status { get; init; } = string.Empty;

    public string? Note { get; init; }
    public DateTime ChangedAt { get; init; }
}
