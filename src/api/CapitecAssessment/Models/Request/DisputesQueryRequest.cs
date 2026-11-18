using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

/// <summary>Query-string options for listing the signed-in user's disputes.</summary>
public sealed class DisputesQueryRequest
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;

    /// <summary>1-based page number.</summary>
    [Range(1, 1_000_000, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "PageSize must be between 1 and 50.")]
    public int PageSize { get; init; } = DefaultPageSize;
}
