using Azure.Communication.Email;
using Azure.Core;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email;

/// <summary>
/// Configuration settings for an Azure Communication Services <see cref="EmailClient"/>.
/// </summary>
public sealed class AzureCommunicationEmailSettings
{
    /// <summary>
    /// Gets or sets the Azure Communication Services HTTPS endpoint or local emulator HTTP endpoint.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the access key used for HMAC authentication.
    /// Local emulators receive a non-secret placeholder key.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// Gets or sets the default verified sender address supplied by the Aspire resource.
    /// </summary>
    public string? SenderAddress { get; set; }

    /// <summary>
    /// Gets or sets the Azure credential. When omitted, <c>DefaultAzureCredential</c> is used.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets the options passed to the Azure Communication Services <see cref="EmailClient"/>.
    /// </summary>
    public EmailClientOptions ClientOptions { get; } = new();
}
