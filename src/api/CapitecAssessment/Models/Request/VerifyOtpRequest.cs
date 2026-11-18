using System.ComponentModel.DataAnnotations;

namespace CapitecAssessment.Models.Request;

public sealed class VerifyOtpRequest : SignInRequestBase
{
    [Required]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "Code must be exactly 6 digits.")]
    public string Code { get; init; } = string.Empty;
}
