namespace CapitecAssessment.Models.Response;

public sealed class RequestOtpResponse
{
    /// <summary>Identical whether or not the account exists, so the endpoint cannot be used to probe account numbers.</summary>
    public const string GenericMessage =
        "If the account number is registered, a one-time pin has been sent to the email address on file. It is valid for up to 5 minutes.";

    public string Message { get; init; } = GenericMessage;
}
