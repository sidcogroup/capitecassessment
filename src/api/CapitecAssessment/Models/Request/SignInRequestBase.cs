using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CapitecAssessment.Models.Request;

/// <summary>
/// Shared by both sign-in steps. Send <c>identifier</c>: a 10-digit account number (customers) or a
/// username (consultants). <c>accountNumber</c> is still accepted as an alias for older clients.
/// </summary>
public abstract class SignInRequestBase : IValidatableObject
{
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>Deprecated alias for <see cref="Identifier"/>.</summary>
    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; init; }

    /// <summary>The value to look the user up by, whichever field it arrived in.</summary>
    [JsonIgnore]
    public string ResolvedIdentifier => Identifier ?? AccountNumber ?? string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Identifier is not null && AccountNumber is not null && Identifier != AccountNumber)
        {
            yield return new ValidationResult(
                "Send either identifier or accountNumber, not two different values.", [nameof(Identifier)]);
        }
        else if (!SignInIdentifier.IsValid(ResolvedIdentifier))
        {
            yield return new ValidationResult(SignInIdentifier.ErrorMessage, [nameof(Identifier)]);
        }
    }
}
