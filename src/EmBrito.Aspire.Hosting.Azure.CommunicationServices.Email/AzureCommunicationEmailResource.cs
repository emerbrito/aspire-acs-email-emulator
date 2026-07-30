using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.Communication;
using Azure.Provisioning.Primitives;

namespace EmBrito.Aspire.Hosting.Azure.CommunicationServices;

/// <summary>
/// Represents an Azure Communication Services resource configured for email.
/// </summary>
/// <param name="name">The Aspire resource name.</param>
/// <param name="configureInfrastructure">The callback that builds the Azure infrastructure.</param>
public sealed class AzureCommunicationEmailResource(
    string name,
    Action<AzureResourceInfrastructure> configureInfrastructure)
    : AzureProvisioningResource(name, configureInfrastructure), IResourceWithConnectionString, IResourceWithEndpoints
{
    internal const string HttpEndpointName = "http";
    internal const string DefaultDataLocation = "United States";
    internal const string DefaultEmulatorSenderAddress = "donotreply@localhost";
    internal const string DefaultEmulatorAccessKey =
        "bG9jYWwtYWNzLWVtYWlsLWVtdWxhdG9yLWtleQ==";

    private EndpointReference HttpEndpoint => new(this, HttpEndpointName);

    internal string DataLocation { get; set; } = DefaultDataLocation;

    internal bool UserEngagementTrackingEnabled { get; set; }

    internal bool AzureManagedDomainEnabled { get; set; } = true;

    internal ExistingEmailDomainConfiguration? ExistingDomain { get; set; }

    internal List<AzureCommunicationEmailDomain> CustomDomains { get; } = [];

    internal VerifiedEmailDomainConfiguration? VerifiedDomain { get; set; }

    /// <summary>
    /// Gets the service endpoint output.
    /// </summary>
    public BicepOutputReference Endpoint => new("endpoint", this);

    /// <summary>
    /// Gets the configured sender address output.
    /// In a custom-domain staging deployment this address is not usable until the domain is verified and linked.
    /// </summary>
    public BicepOutputReference SenderAddress => new("senderAddress", this);

    /// <summary>
    /// Gets the Azure Communication Services resource name output.
    /// </summary>
    public BicepOutputReference NameOutputReference => new("name", this);

    /// <summary>
    /// Gets the Azure Communication Services resource ID output.
    /// </summary>
    public BicepOutputReference Id => new("id", this);

    /// <summary>
    /// Gets the generated Email Communication Service name output when using the managed domain.
    /// </summary>
    public BicepOutputReference EmailServiceName => new("emailServiceName", this);

    /// <summary>
    /// Gets the generated Email Communication Services domain name output when using the managed domain.
    /// </summary>
    public BicepOutputReference DomainName => new("domainName", this);

    /// <summary>
    /// Gets a value indicating whether this resource is configured to run as a local emulator.
    /// </summary>
    public bool IsEmulator => this.IsContainer();

    /// <summary>
    /// Gets the endpoint expression used by application client integrations.
    /// </summary>
    public ReferenceExpression EndpointExpression => IsEmulator
        ? ReferenceExpression.Create($"http://{HttpEndpoint.Property(EndpointProperty.HostAndPort)}")
        : ReferenceExpression.Create($"{Endpoint}");

    /// <summary>
    /// Gets the sender-address expression used by application client integrations.
    /// </summary>
    public ReferenceExpression SenderAddressExpression => IsEmulator
        ? ReferenceExpression.Create($"{DefaultEmulatorSenderAddress}")
        : ReferenceExpression.Create($"{SenderAddress}");

    /// <summary>
    /// Gets a connection string containing the endpoint, default sender address, and
    /// local emulator credential when applicable.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => IsEmulator
        ? ReferenceExpression.Create(
            $"Endpoint={EndpointExpression};AccessKey={DefaultEmulatorAccessKey};SenderAddress={SenderAddressExpression}")
        : ReferenceExpression.Create(
            $"Endpoint={EndpointExpression};SenderAddress={SenderAddressExpression}");

    /// <inheritdoc />
    public override ProvisionableResource AddAsExistingResource(AzureResourceInfrastructure infra)
    {
        var bicepIdentifier = this.GetBicepIdentifier();
        var existing = infra.GetProvisionableResources()
            .OfType<CommunicationService>()
            .SingleOrDefault(resource => resource.BicepIdentifier == bicepIdentifier);

        if (existing is not null)
        {
            return existing;
        }

        var communicationService = CommunicationService.FromExisting(bicepIdentifier);

        if (!TryApplyExistingResourceAnnotation(this, infra, communicationService))
        {
            communicationService.Name = NameOutputReference.AsProvisioningParameter(infra);
        }

        infra.Add(communicationService);
        return communicationService;
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Endpoint", EndpointExpression);
        yield return new("SenderAddress", SenderAddressExpression);
        yield return new(
            "Authentication",
            IsEmulator
                ? ReferenceExpression.Create($"HMAC")
                : ReferenceExpression.Create($"MicrosoftEntraId"));

        if (IsEmulator)
        {
            yield return new("AccessKey", ReferenceExpression.Create($"{DefaultEmulatorAccessKey}"));
        }
    }

    internal sealed record ExistingEmailDomainConfiguration(string ResourceId, string SenderAddress);

    internal sealed record VerifiedEmailDomainConfiguration(
        AzureCommunicationEmailDomain Domain,
        string SenderAddress);
}
