using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

public sealed class RequestOtpRequest
{
    /// <summary>10-digit account number. [0-9], not \d, so non-ASCII digits are rejected.</summary>
    [Required]
    [RegularExpression("^[0-9]{10}$", ErrorMessage = "Account number must be exactly 10 digits.")]
    public string AccountNumber { get; init; } = string.Empty;
}
