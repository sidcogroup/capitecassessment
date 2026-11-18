namespace CapitecAssessment.Models.Response;

/// <summary>One row of the consultant's client list.</summary>
public sealed class ClientSummaryResponse
{
    public long UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
