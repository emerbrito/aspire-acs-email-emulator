using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using EmBrito.Aspire.Hosting;
using EmBrito.Aspire.Hosting.Azure.CommunicationServices;
using Xunit;

namespace EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email.Tests;

public sealed class AzureCommunicationEmailExtensionsTests
{
    [Fact]
    public void AddAzureCommunicationEmailAddsProvisioningAndDefaultRole()
    {
        var builder = DistributedApplication.CreateBuilder();

        var email = builder.AddAzureCommunicationEmail("email");

        Assert.Equal("email", email.Resource.Name);
        Assert.False(email.Resource.IsEmulator);
        Assert.Equal(
            "Endpoint={email.outputs.endpoint};SenderAddress={email.outputs.senderAddress}",
            email.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Contains(
            email.Resource.Annotations,
            annotation => annotation is DefaultRoleAssignmentsAnnotation);
    }

    [Fact]
    public void RunAsEmulatorUsesPinnedRestEmulatorAndExpectedEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        var email = builder
            .AddAzureCommunicationEmail("email")
            .RunAsEmulator();

        Assert.True(email.Resource.IsEmulator);
        Assert.Contains(
            email.Resource.Annotations,
            annotation => annotation is EmulatorResourceAnnotation);

        var image = Assert.Single(
            email.Resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal(
            "emerbrito/aspire-acs-email-emulator",
            image.Image);
        var packageVersion = typeof(AzureCommunicationEmailExtensions)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Split('+', 2)[0];
        Assert.Equal(packageVersion, image.Tag);

        var endpoint = Assert.Single(
            email.Resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Contains("Endpoint=http://", email.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Contains(
            "AccessKey=bG9jYWwtYWNzLWVtYWlsLWVtdWxhdG9yLWtleQ==",
            email.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Contains(
            "SenderAddress=donotreply@localhost",
            email.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void EmulatorCanUseNamedVolumeAndFixedHostPort()
    {
        var builder = DistributedApplication.CreateBuilder();

        var email = builder
            .AddAzureCommunicationEmail("email")
            .RunAsEmulator(
                emulator => emulator
                    .WithDataVolume("email-data")
                    .WithHostPort(18080));

        var mount = Assert.Single(
            email.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("email-data", mount.Source);
        Assert.Equal("/data", mount.Target);
        Assert.Equal(ContainerMountType.Volume, mount.Type);

        var endpoint = Assert.Single(
            email.Resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(18080, endpoint.Port);
    }

    [Fact]
    public void EmulatorCanUseDataBindMount()
    {
        var builder = DistributedApplication.CreateBuilder();

        var email = builder
            .AddAzureCommunicationEmail("email")
            .RunAsEmulator(
                emulator => emulator.WithDataBindMount("email-inbox"));

        var mount = Assert.Single(
            email.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.EndsWith("email-inbox", mount.Source, StringComparison.Ordinal);
        Assert.Equal("/data", mount.Target);
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
    }

    [Fact]
    public void RunAsEmulatorCannotBeAppliedTwice()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email").RunAsEmulator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => email.RunAsEmulator());

        Assert.Contains("already configured", exception.Message);
    }

    [Fact]
    public void ExistingVerifiedDomainIsValidatedAndStored()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        const string domainId =
            "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/" +
            "providers/Microsoft.Communication/emailServices/mail/domains/example.com";

        email.WithExistingVerifiedDomain(domainId, "notifications@example.com");

        Assert.Equal(domainId, email.Resource.ExistingDomain?.ResourceId);
        Assert.Equal(
            "notifications@example.com",
            email.Resource.ExistingDomain?.SenderAddress);
    }

    [Fact]
    public void CustomDomainIsDeclaredWithoutReplacingManagedDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");

        var domain = email.AddCustomDomain("mail.example.com");

        Assert.Equal("mail.example.com", domain.Name);
        Assert.Same(email.Resource, domain.Owner);
        Assert.True(email.Resource.AzureManagedDomainEnabled);
        Assert.Same(domain, Assert.Single(email.Resource.CustomDomains));
        Assert.Null(email.Resource.VerifiedDomain);
    }

    [Fact]
    public void VerifiedCustomDomainIsSelectedWithItsSender()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        var domain = email.AddCustomDomain("mail.example.com");

        email.WithVerifiedDomain(domain, "notifications@mail.example.com");

        Assert.Same(domain, email.Resource.VerifiedDomain?.Domain);
        Assert.Equal(
            "notifications@mail.example.com",
            email.Resource.VerifiedDomain?.SenderAddress);
    }

    [Fact]
    public void AzureManagedDomainCanBeDisabledExplicitly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder
            .AddAzureCommunicationEmail("email")
            .WithoutAzureManagedDomain();

        email.AddCustomDomain("mail.example.com");

        Assert.False(email.Resource.AzureManagedDomainEnabled);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("not a domain")]
    [InlineData("https://example.com")]
    public void CustomDomainRejectsInvalidDnsNames(string domainName)
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");

        Assert.Throws<ArgumentException>(() => email.AddCustomDomain(domainName));
    }

    [Fact]
    public void CustomDomainCannotBeAddedTwice()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        email.AddCustomDomain("mail.example.com");

        Assert.Throws<InvalidOperationException>(
            () => email.AddCustomDomain("MAIL.EXAMPLE.COM"));
    }

    [Fact]
    public void VerifiedDomainMustBelongToTheEmailResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var first = builder.AddAzureCommunicationEmail("first");
        var second = builder.AddAzureCommunicationEmail("second");
        var domain = first.AddCustomDomain("mail.example.com");

        Assert.Throws<ArgumentException>(
            () => second.WithVerifiedDomain(domain, "notifications@mail.example.com"));
    }

    [Fact]
    public void VerifiedSenderMustUseTheSelectedDomain()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        var domain = email.AddCustomDomain("mail.example.com");

        Assert.Throws<ArgumentException>(
            () => email.WithVerifiedDomain(domain, "notifications@example.com"));
    }

    [Fact]
    public void OwnedAndExistingDomainsAreMutuallyExclusive()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        email.AddCustomDomain("mail.example.com");
        const string domainId =
            "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/" +
            "providers/Microsoft.Communication/emailServices/mail/domains/external.example.com";

        Assert.Throws<InvalidOperationException>(
            () => email.WithExistingVerifiedDomain(
                domainId,
                "notifications@external.example.com"));
    }

    [Fact]
    public void PendingCustomDomainKeepsManagedDomainLinkedInBicep()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        email.AddCustomDomain("mail.example.com");

        var bicep = email.Resource.GetBicepTemplateString();

        Assert.Contains("resource managedDomain", bicep);
        Assert.Contains("resource customDomain0", bicep);
        Assert.Contains("name: 'mail.example.com'", bicep);
        Assert.Matches(
            @"linkedDomains:\s*\[\s*managedDomain\.id\s*\]",
            bicep);
    }

    [Fact]
    public void VerifiedCustomDomainBecomesTheOnlyLinkedDomainInBicep()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");
        var domain = email.AddCustomDomain("mail.example.com");
        email.WithVerifiedDomain(domain, "notifications@mail.example.com");

        var bicep = email.Resource.GetBicepTemplateString();

        Assert.Contains("resource managedDomain", bicep);
        Assert.Matches(
            @"linkedDomains:\s*\[\s*customDomain0\.id\s*\]",
            bicep);
        Assert.DoesNotMatch(
            @"linkedDomains:\s*\[\s*managedDomain\.id\s*\]",
            bicep);
        Assert.Contains(
            "output senderAddress string = 'notifications@mail.example.com'",
            bicep);
    }

    [Fact]
    public void ManagedDomainOptOutOmitsItAndLeavesPendingCustomDomainUnlinked()
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder
            .AddAzureCommunicationEmail("email")
            .WithoutAzureManagedDomain();
        email.AddCustomDomain("mail.example.com");

        var bicep = email.Resource.GetBicepTemplateString();

        Assert.DoesNotContain("resource managedDomain", bicep);
        Assert.Contains("resource customDomain0", bicep);
        Assert.DoesNotContain("linkedDomains:", bicep);
    }

    [Fact]
    public void FluentSettingsAreStoredOnTheResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        var email = builder
            .AddAzureCommunicationEmail("email")
            .WithDataLocation("Europe")
            .WithUserEngagementTracking();

        Assert.Equal("Europe", email.Resource.DataLocation);
        Assert.True(email.Resource.UserEngagementTrackingEnabled);
        Assert.Equal(
            "{email.outputs.emailServiceName}",
            email.Resource.EmailServiceName.ValueExpression);
        Assert.Equal(
            "{email.outputs.domainName}",
            email.Resource.DomainName.ValueExpression);
    }

    [Theory]
    [InlineData("not-an-id", "sender@example.com")]
    [InlineData(
        "/subscriptions/id/providers/Microsoft.Communication/communicationServices/name",
        "sender@example.com")]
    [InlineData(
        "/subscriptions/id/providers/Microsoft.Communication/emailServices/mail/domains/example.com",
        "not-an-email")]
    public void ExistingVerifiedDomainRejectsInvalidValues(
        string domainResourceId,
        string senderAddress)
    {
        var builder = DistributedApplication.CreateBuilder();
        var email = builder.AddAzureCommunicationEmail("email");

        Assert.Throws<ArgumentException>(
            () => email.WithExistingVerifiedDomain(domainResourceId, senderAddress));
    }
}
