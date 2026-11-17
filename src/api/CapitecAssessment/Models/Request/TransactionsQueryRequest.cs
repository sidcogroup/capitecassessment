using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

/// <summary>Query-string options for listing an account's transactions.</summary>
public sealed class TransactionsQueryRequest
{
    public const int DefaultDays = 30;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>How far back to look: 30, 60 or 90 days.</summary>
    [AllowedValues(30, 60, 90, ErrorMessage = "Days must be 30, 60 or 90.")]
    public int Days { get; init; } = DefaultDays;

    /// <summary>1-based page number.</summary>
    [Range(1, 1_000_000, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = DefaultPageSize;
}
