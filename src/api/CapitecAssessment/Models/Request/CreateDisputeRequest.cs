using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

/// <summary>Body for raising a dispute. Status and timestamps are set by the server.</summary>
public sealed class CreateDisputeRequest
{
    [Required]
    public Guid TransactionId { get; init; }

    [Required(AllowEmptyStrings = false), StringLength(100)]
    public string Reason { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Details { get; init; }
}
