namespace CapitecAssessment.Models.Response;

/// <summary>A client's identity, profile and contact details, as seen by a consultant.</summary>
public sealed class ClientProfileResponse
{
    public long UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? ContactNumber { get; init; }
    public string? ContactEmail { get; init; }
    public DateTime CreatedAt { get; init; }
}
