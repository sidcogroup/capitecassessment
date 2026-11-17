using Microsoft.Extensions.Options;

namespace CapitecAssessment.Configuration;

/// <summary>
/// Azure Communication Services Email. Bound from "AzureCommunicationServices:Email".
/// Keep the connection string out of committed files: use user-secrets or the
/// AzureCommunicationServices__Email__ConnectionString environment variable.
/// </summary>
public sealed class AzureCommunicationEmailOptions
{
    public const string SectionName = "AzureCommunicationServices:Email";

    /// <summary>"endpoint=https://&lt;resource&gt;.communication.azure.com/;accesskey=..." from the ACS resource's Keys blade.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>A MailFrom address on a domain linked to the ACS resource, e.g. DoNotReply@&lt;guid&gt;.azurecomm.net.</summary>
    public string SenderAddress { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

/// <summary>
/// Outside Development, ACS must be configured (the app refuses to start otherwise).
/// In Development it may be left empty, and emails are written to the log instead.
/// </summary>
public sealed class AzureCommunicationEmailOptionsValidator : IValidateOptions<AzureCommunicationEmailOptions>
{
    private readonly IHostEnvironment _environment;

    public AzureCommunicationEmailOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, AzureCommunicationEmailOptions options)
    {
        if (!options.IsConfigured)
        {
            return _environment.IsDevelopment()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"{SectionName}:ConnectionString is required outside Development.");
        }

        return string.IsNullOrWhiteSpace(options.SenderAddress)
            ? ValidateOptionsResult.Fail($"{SectionName}:SenderAddress is required when a connection string is set.")
            : ValidateOptionsResult.Success;
    }

    private const string SectionName = AzureCommunicationEmailOptions.SectionName;
}
