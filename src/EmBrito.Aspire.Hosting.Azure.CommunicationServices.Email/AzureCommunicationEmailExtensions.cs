#pragma warning disable ASPIREAZURE003

using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using EmBrito.Aspire.Hosting.Azure.CommunicationServices;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Communication;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Primitives;

namespace EmBrito.Aspire.Hosting;

/// <summary>
/// Extension methods for adding Azure Communication Services Email resources to an Aspire application.
/// </summary>
public static class AzureCommunicationEmailExtensions
{
    /// <summary>
    /// Adds Azure Communication Services configured for email.
    /// </summary>
    /// <remarks>
    /// The default Azure deployment creates an Email Communication Service, an Azure-managed domain,
    /// links that domain to Communication Services, and exposes its verified
    /// <c>donotreply</c> sender address.
    /// </remarks>
    public static IResourceBuilder<AzureCommunicationEmailResource> AddAzureCommunicationEmail(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.AddAzureProvisioning();

        var resource = new AzureCommunicationEmailResource(name, ConfigureInfrastructure);
        var roles = new HashSet<RoleDefinition>
        {
            new(
                AzureCommunicationEmailBuiltInRole.CommunicationAndEmailServiceOwner.ToString(),
                AzureCommunicationEmailBuiltInRole.GetBuiltInRoleName(
                    AzureCommunicationEmailBuiltInRole.CommunicationAndEmailServiceOwner))
        };

        return builder.AddResource(resource)
            .WithAnnotation(new DefaultRoleAssignmentsAnnotation(roles));

        void ConfigureInfrastructure(AzureResourceInfrastructure infrastructure)
        {
            var emailResource = (AzureCommunicationEmailResource)infrastructure.AspireResource;
            CommunicationDomain? managedDomain = null;
            EmailService? emailService = null;

            BicepValue<string>? linkedDomainId = null;
            BicepValue<string> senderAddress;

            if (emailResource.ExistingDomain is { } existingDomain)
            {
                linkedDomainId = existingDomain.ResourceId;
                senderAddress = existingDomain.SenderAddress;
            }
            else
            {
                emailService = new EmailService("emailService")
                {
                    Location = new AzureLocation("Global"),
                    DataLocation = emailResource.DataLocation,
                    Tags = { { "aspire-resource-name", $"{emailResource.Name}-email" } }
                };
                infrastructure.Add(emailService);

                if (emailResource.AzureManagedDomainEnabled)
                {
                    managedDomain = new CommunicationDomain("managedDomain")
                    {
                        Parent = emailService,
                        Name = "AzureManagedDomain",
                        Location = new AzureLocation("Global"),
                        DomainManagement = DomainManagement.AzureManaged,
                        UserEngagementTracking = emailResource.UserEngagementTrackingEnabled
                            ? UserEngagementTracking.Enabled
                            : UserEngagementTracking.Disabled,
                        Tags = { { "aspire-resource-name", $"{emailResource.Name}-domain" } }
                    };
                    infrastructure.Add(managedDomain);
                }

                var customDomains = new Dictionary<AzureCommunicationEmailDomain, CommunicationDomain>();
                foreach (var customDomainConfiguration in emailResource.CustomDomains)
                {
                    var customDomain = new CommunicationDomain(customDomainConfiguration.BicepIdentifier)
                    {
                        Parent = emailService,
                        Name = customDomainConfiguration.Name,
                        Location = new AzureLocation("Global"),
                        DomainManagement = DomainManagement.CustomerManaged,
                        UserEngagementTracking = emailResource.UserEngagementTrackingEnabled
                            ? UserEngagementTracking.Enabled
                            : UserEngagementTracking.Disabled,
                        Tags =
                        {
                            { "aspire-resource-name", $"{emailResource.Name}-{customDomainConfiguration.Name}" }
                        }
                    };
                    infrastructure.Add(customDomain);
                    customDomains.Add(customDomainConfiguration, customDomain);
                }

                if (emailResource.VerifiedDomain is { } verifiedDomain)
                {
                    linkedDomainId = customDomains[verifiedDomain.Domain].Id;
                    senderAddress = verifiedDomain.SenderAddress;
                }
                else if (managedDomain is not null)
                {
                    linkedDomainId = managedDomain.Id;
                    senderAddress =
                        BicepFunction.Interpolate($"donotreply@{managedDomain.MailFromSenderDomain}");
                }
                else if (emailResource.CustomDomains is [var pendingDomain, ..])
                {
                    // This address is intentionally only a syntactically valid staging value.
                    // Azure cannot send from it until the domain is verified and linked.
                    senderAddress = $"donotreply@{pendingDomain.Name}";
                }
                else
                {
                    throw new InvalidOperationException(
                        "An email resource without an Azure-managed domain must declare a custom domain.");
                }
            }

            var communicationService =
                AzureProvisioningResource.CreateExistingOrNewProvisionableResource(
                    infrastructure,
                    (identifier, resourceName) =>
                    {
                        var existing = CommunicationService.FromExisting(identifier);
                        existing.Name = resourceName;
                        return existing;
                    },
                    _ =>
                    {
                        var resource =
                            new CommunicationService(infrastructure.AspireResource.GetBicepIdentifier())
                            {
                                Location = new AzureLocation("Global"),
                                DataLocation = emailResource.DataLocation,
                                Tags = { { "aspire-resource-name", emailResource.Name } }
                            };

                        if (linkedDomainId is not null)
                        {
                            resource.LinkedDomains.Add(linkedDomainId);
                        }

                        return resource;
                    });

            infrastructure.Add(new ProvisioningOutput("endpoint", typeof(string))
            {
                Value = BicepFunction.Interpolate($"https://{communicationService.HostName}")
            });
            infrastructure.Add(new ProvisioningOutput("senderAddress", typeof(string))
            {
                Value = senderAddress
            });
            infrastructure.Add(new ProvisioningOutput("name", typeof(string))
            {
                Value = communicationService.Name
            });
            infrastructure.Add(new ProvisioningOutput("id", typeof(string))
            {
                Value = communicationService.Id
            });

            if (emailService is not null)
            {
                infrastructure.Add(new ProvisioningOutput("emailServiceName", typeof(string))
                {
                    Value = emailService.Name
                });
            }

            if (managedDomain is not null)
            {
                infrastructure.Add(new ProvisioningOutput("domainName", typeof(string))
                {
                    Value = managedDomain.Name
                });
            }
        }
    }

    /// <summary>
    /// Sets the Azure geography where Communication Services stores email data at rest.
    /// </summary>
    public static IResourceBuilder<AzureCommunicationEmailResource> WithDataLocation(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        string dataLocation)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataLocation);

        builder.Resource.DataLocation = dataLocation;
        return builder;
    }

    /// <summary>
    /// Enables or disables open and click engagement tracking for domains owned by this integration.
    /// </summary>
    /// <remarks>
    /// Tracking is disabled by default. Applications sending regulated or privacy-sensitive
    /// email should evaluate consent and policy requirements before enabling it.
    /// </remarks>
    public static IResourceBuilder<AzureCommunicationEmailResource> WithUserEngagementTracking(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.UserEngagementTrackingEnabled = enabled;
        return builder;
    }

    /// <summary>
    /// Declares a customer-managed domain as a child of the Email Communication Service.
    /// </summary>
    /// <remarks>
    /// The domain is provisioned but is not linked to Communication Services until
    /// <see cref="WithVerifiedDomain"/> is called after Azure reports that domain ownership,
    /// SPF, DKIM, and DKIM2 are verified.
    /// </remarks>
    /// <param name="builder">The email resource builder.</param>
    /// <param name="domainName">The fully qualified domain name, such as <c>mail.contoso.com</c>.</param>
    /// <returns>A domain descriptor that can later be passed to <see cref="WithVerifiedDomain"/>.</returns>
    public static AzureCommunicationEmailDomain AddCustomDomain(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        string domainName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainName);

        if (Uri.CheckHostName(domainName) != UriHostNameType.Dns
            || !domainName.Contains('.', StringComparison.Ordinal)
            || domainName.StartsWith('.')
            || domainName.EndsWith('.'))
        {
            throw new ArgumentException(
                "The value must be a fully qualified DNS domain name.",
                nameof(domainName));
        }

        if (builder.Resource.ExistingDomain is not null)
        {
            throw new InvalidOperationException(
                "A resource configured with an existing verified domain cannot also own a custom domain.");
        }

        if (builder.Resource.CustomDomains.Any(
                domain => string.Equals(domain.Name, domainName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"The custom domain '{domainName}' has already been added.");
        }

        var domain = new AzureCommunicationEmailDomain(
            builder.Resource,
            domainName,
            builder.Resource.CustomDomains.Count);
        builder.Resource.CustomDomains.Add(domain);
        return domain;
    }

    /// <summary>
    /// Declares that a provisioned customer-managed domain is verified and links it to
    /// Communication Services as the active sending domain.
    /// </summary>
    /// <remarks>
    /// This method does not perform or bypass Azure's DNS verification. Call it only after
    /// Azure reports Domain, SPF, DKIM, and DKIM2 as verified. Deployment fails if Azure
    /// does not accept the domain as verified.
    /// </remarks>
    public static IResourceBuilder<AzureCommunicationEmailResource> WithVerifiedDomain(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        AzureCommunicationEmailDomain domain,
        string senderAddress)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);

        if (!ReferenceEquals(builder.Resource, domain.Owner))
        {
            throw new ArgumentException(
                "The domain must belong to the same Azure Communication Services Email resource.",
                nameof(domain));
        }

        ValidateSenderAddress(senderAddress);

        var senderDomain = new System.Net.Mail.MailAddress(senderAddress).Host;
        if (!string.Equals(senderDomain, domain.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The sender address must use the custom domain '{domain.Name}'.",
                nameof(senderAddress));
        }

        builder.Resource.VerifiedDomain =
            new AzureCommunicationEmailResource.VerifiedEmailDomainConfiguration(
                domain,
                senderAddress);

        return builder;
    }

    /// <summary>
    /// Prevents the integration from provisioning the default Azure-managed domain.
    /// </summary>
    /// <remarks>
    /// A custom domain must also be declared. Until that domain is verified and selected with
    /// <see cref="WithVerifiedDomain"/>, the Communication Services resource has no linked
    /// email domain and cannot send email.
    /// </remarks>
    public static IResourceBuilder<AzureCommunicationEmailResource> WithoutAzureManagedDomain(
        this IResourceBuilder<AzureCommunicationEmailResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.AzureManagedDomainEnabled = false;
        return builder;
    }

    /// <summary>
    /// Links an existing, verified Email Communication Services domain instead of creating an Azure-managed domain.
    /// </summary>
    /// <param name="builder">The email resource builder.</param>
    /// <param name="domainResourceId">
    /// The full Azure resource ID of a verified <c>Microsoft.Communication/emailServices/domains</c> resource.
    /// </param>
    /// <param name="senderAddress">A verified sender address on that domain.</param>
    public static IResourceBuilder<AzureCommunicationEmailResource> WithExistingVerifiedDomain(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        string domainResourceId,
        string senderAddress)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(domainResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);

        if (!domainResourceId.StartsWith(
                "/subscriptions/",
                StringComparison.OrdinalIgnoreCase)
            || !domainResourceId.Contains(
                "/providers/Microsoft.Communication/emailServices/",
                StringComparison.OrdinalIgnoreCase)
            || !domainResourceId.Contains("/domains/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The value must be a full Microsoft.Communication/emailServices/domains resource ID.",
                nameof(domainResourceId));
        }

        if (!System.Net.Mail.MailAddress.TryCreate(senderAddress, out _))
        {
            throw new ArgumentException("The value must be a valid email address.", nameof(senderAddress));
        }

        if (builder.Resource.CustomDomains.Count > 0)
        {
            throw new InvalidOperationException(
                "A resource that owns custom domains cannot use an externally owned domain.");
        }

        builder.Resource.ExistingDomain =
            new AzureCommunicationEmailResource.ExistingEmailDomainConfiguration(
                domainResourceId,
                senderAddress);

        return builder;
    }

    private static void ValidateSenderAddress(string senderAddress)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(senderAddress, out _))
        {
            throw new ArgumentException("The value must be a valid email address.", nameof(senderAddress));
        }
    }

    /// <summary>
    /// Configures the resource to run locally as an Azure Communication Services Email
    /// REST API emulator and inbox.
    /// </summary>
    public static IResourceBuilder<AzureCommunicationEmailResource> RunAsEmulator(
        this IResourceBuilder<AzureCommunicationEmailResource> builder,
        Action<IResourceBuilder<AzureCommunicationEmailEmulatorResource>>? configureContainer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Resource.IsEmulator)
        {
            throw new InvalidOperationException(
                "The Azure Communication Services Email resource is already configured to run as an emulator.");
        }

        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        builder.WithAnnotation(new EmulatorResourceAnnotation());
        builder
            .WithHttpEndpoint(
                name: AzureCommunicationEmailResource.HttpEndpointName,
                targetPort: 8080)
            .WithAnnotation(new ContainerImageAnnotation
            {
                Registry = EmailEmulatorContainerImageTags.Registry,
                Image = EmailEmulatorContainerImageTags.Image,
                Tag = EmailEmulatorContainerImageTags.Tag
            })
            .WithUrlForEndpoint(
                AzureCommunicationEmailResource.HttpEndpointName,
                url =>
                {
                    url.DisplayText = "Email inbox";
                    url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
                })
            .WithHttpHealthCheck(
                path: "/livez",
                endpointName: AzureCommunicationEmailResource.HttpEndpointName);

        var surrogate = new AzureCommunicationEmailEmulatorResource(builder.Resource);
        var surrogateBuilder = builder.ApplicationBuilder.CreateResourceBuilder(surrogate);
        surrogateBuilder.WithEnvironment("Emulator__DatabasePath", "/data/email.db");
        configureContainer?.Invoke(surrogateBuilder);

        return builder;
    }

    /// <summary>
    /// Adds a named volume for captured emulator messages.
    /// </summary>
    public static IResourceBuilder<AzureCommunicationEmailEmulatorResource> WithDataVolume(
        this IResourceBuilder<AzureCommunicationEmailEmulatorResource> builder,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(
            name ?? VolumeNameGenerator.Generate(builder, "data"),
            "/data");
    }

    /// <summary>
    /// Adds a bind mount for captured emulator messages.
    /// </summary>
    public static IResourceBuilder<AzureCommunicationEmailEmulatorResource> WithDataBindMount(
        this IResourceBuilder<AzureCommunicationEmailEmulatorResource> builder,
        string? path = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithBindMount(
            path ?? $".email-emulator/{builder.Resource.Name}",
            "/data");
    }

    /// <summary>
    /// Sets the host port for the emulator REST API and inbox.
    /// </summary>
    public static IResourceBuilder<AzureCommunicationEmailEmulatorResource> WithHostPort(
        this IResourceBuilder<AzureCommunicationEmailEmulatorResource> builder,
        int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        return builder.WithEndpoint(
            AzureCommunicationEmailResource.HttpEndpointName,
            endpoint => endpoint.Port = port);
    }

    /// <summary>
    /// Assigns Communication Services roles to a referencing resource, replacing the target's defaults for that reference.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AzureCommunicationEmailResource> target,
        params AzureCommunicationEmailBuiltInRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);

        var definitions = roles
            .Select(role => new RoleDefinition(
                role.ToString(),
                AzureCommunicationEmailBuiltInRole.GetBuiltInRoleName(role)))
            .ToHashSet();

        return builder.WithAnnotation(new RoleAssignmentAnnotation(target.Resource, definitions));
    }

    private static class EmailEmulatorContainerImageTags
    {
        internal const string Registry = "ghcr.io";
        internal const string Image =
            "emerbrito/aspire-acs-email-emulator";
        internal static string Tag { get; } = GetPackageVersion();
    }

    private static string GetPackageVersion()
    {
        var informationalVersion = typeof(AzureCommunicationEmailExtensions)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException(
                "The hosting assembly does not declare an informational version.");
        }

        var buildMetadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return buildMetadataSeparator < 0
            ? informationalVersion
            : informationalVersion[..buildMetadataSeparator];
    }
}
