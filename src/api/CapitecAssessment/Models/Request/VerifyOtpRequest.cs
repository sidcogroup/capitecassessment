using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

public sealed class VerifyOtpRequest
{
    [Required]
    [RegularExpression("^[0-9]{10}$", ErrorMessage = "Account number must be exactly 10 digits.")]
    public string AccountNumber { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "Code must be exactly 6 digits.")]
    public string Code { get; init; } = string.Empty;
}
