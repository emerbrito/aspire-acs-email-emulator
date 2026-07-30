using Azure.Communication.Email;
using EmBrito.Aspire.Azure.CommunicationServices.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Tests;

public sealed class AspireCommunicationEmailExtensionsTests
{
    private const string LocalAccessKey =
        "bG9jYWwtYWNzLWVtYWlsLWVtdWxhdG9yLWtleQ==";

    [Fact]
    public void RegistersFirstPartyClientForLocalHmacConnection()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(
        [
            new(
                "ConnectionStrings:email",
                $"Endpoint=http://localhost:8080;AccessKey={LocalAccessKey};SenderAddress=donotreply@localhost")
        ]);

        builder.AddAzureCommunicationEmailClient("email");

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<EmailClient>();
        var settings = host.Services.GetRequiredService<AzureCommunicationEmailSettings>();

        Assert.NotNull(client);
        Assert.Equal("http://localhost:8080", settings.Endpoint);
        Assert.Equal(LocalAccessKey, settings.AccessKey);
        Assert.Equal("donotreply@localhost", settings.SenderAddress);
    }

    [Fact]
    public void RegistersFirstPartyClientForAzureIdentityConnection()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(
        [
            new(
                "ConnectionStrings:email",
                "Endpoint=https://example.communication.azure.com;SenderAddress=donotreply@example.azurecomm.net")
        ]);

        builder.AddAzureCommunicationEmailClient("email");

        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<AzureCommunicationEmailSettings>();

        Assert.NotNull(host.Services.GetRequiredService<EmailClient>());
        Assert.Null(settings.AccessKey);
    }

    [Fact]
    public void SupportsMultipleKeyedClientsAndSettings()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(
        [
            new(
                "ConnectionStrings:first",
                $"Endpoint=http://localhost:1025;AccessKey={LocalAccessKey};SenderAddress=first@localhost"),
            new(
                "ConnectionStrings:second",
                $"Endpoint=http://localhost:2025;AccessKey={LocalAccessKey};SenderAddress=second@localhost")
        ]);

        builder.AddKeyedAzureCommunicationEmailClient("first");
        builder.AddKeyedAzureCommunicationEmailClient("second");

        using var host = builder.Build();
        var firstClient = host.Services.GetRequiredKeyedService<EmailClient>("first");
        var secondClient = host.Services.GetRequiredKeyedService<EmailClient>("second");
        var firstSettings = host.Services
            .GetRequiredKeyedService<AzureCommunicationEmailSettings>("first");
        var secondSettings = host.Services
            .GetRequiredKeyedService<AzureCommunicationEmailSettings>("second");

        Assert.NotSame(firstClient, secondClient);
        Assert.Equal("first@localhost", firstSettings.SenderAddress);
        Assert.Equal("second@localhost", secondSettings.SenderAddress);
    }

    [Theory]
    [InlineData("Endpoint=http://localhost:8080")]
    [InlineData("SenderAddress=sender@example.com")]
    [InlineData("Endpoint=ftp://localhost;SenderAddress=sender@example.com")]
    [InlineData("Endpoint=http://localhost;SenderAddress=not-an-address")]
    [InlineData("Endpoint=http://localhost;AccessKey=not-base64;SenderAddress=sender@example.com")]
    [InlineData("not-a-connection-string")]
    public void RejectsInvalidConnectionSettings(string connectionString)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(
        [
            new("ConnectionStrings:email", connectionString)
        ]);

        Assert.ThrowsAny<Exception>(
            () => builder.AddAzureCommunicationEmailClient("email"));
    }

    [Fact]
    public void ExplicitSettingsOverrideConfiguration()
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection(
        [
            new(
                "ConnectionStrings:email",
                $"Endpoint=http://localhost:8080;AccessKey={LocalAccessKey};SenderAddress=config@localhost")
        ]);

        builder.AddAzureCommunicationEmailClient(
            "email",
            settings => settings.SenderAddress = "code@localhost");

        using var host = builder.Build();
        Assert.Equal(
            "code@localhost",
            host.Services.GetRequiredService<AzureCommunicationEmailSettings>().SenderAddress);
    }
}
