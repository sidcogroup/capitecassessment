namespace CapitecAssessment.Models;

/// <summary>The user an account number resolves to, with the address their OTP is sent to.</summary>
public sealed record AuthenticatedUser(long UserId, string Username, string Role, string Email);
