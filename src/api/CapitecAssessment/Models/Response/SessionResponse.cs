namespace CapitecAssessment.Models.Response;

/// <summary>Who the session cookie belongs to.</summary>
public sealed class SessionResponse
{
    public long UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
