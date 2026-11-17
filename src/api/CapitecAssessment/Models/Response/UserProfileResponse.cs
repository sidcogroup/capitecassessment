namespace CapitecAssessment.Models.Response;

/// <summary>The signed-in user's personal details, used to show their name in the client.</summary>
public sealed class UserProfileResponse
{
    public long UserId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;

    /// <summary>First and last name joined with a space, e.g. "Lerato Dlamini".</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}
