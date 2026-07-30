using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email;

/// <summary>
/// Extension methods for registering Azure Communication Services Email clients.
/// </summary>
public static class AspireCommunicationEmailExtensions
{
    private const string DefaultConfigSectionName =
        "Aspire:Azure:CommunicationServices:Email";

    /// <summary>
    /// Registers the first-party <see cref="EmailClient"/> using the named Aspire connection.
    /// </summary>
    /// <remarks>
    /// This method only configures dependency injection. It registers Microsoft's concrete
    /// <see cref="EmailClient"/> type and does not wrap, proxy, subclass, or replace it.
    /// </remarks>
    public static void AddAzureCommunicationEmailClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<AzureCommunicationEmailSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var settings = CreateSettings(builder.Configuration, connectionName, configureSettings);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(CreateEmailClient(settings));
    }

    /// <summary>
    /// Registers a keyed first-party <see cref="EmailClient"/> using the named Aspire connection.
    /// </summary>
    /// <remarks>
    /// This method only configures dependency injection. It registers Microsoft's concrete
    /// <see cref="EmailClient"/> type and does not wrap, proxy, subclass, or replace it.
    /// </remarks>
    public static void AddKeyedAzureCommunicationEmailClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<AzureCommunicationEmailSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = CreateSettings(builder.Configuration, name, configureSettings);
        builder.Services.AddKeyedSingleton(name, settings);
        builder.Services.AddKeyedSingleton(name, CreateEmailClient(settings));
    }

    private static AzureCommunicationEmailSettings CreateSettings(
        IConfiguration configuration,
        string connectionName,
        Action<AzureCommunicationEmailSettings>? configureSettings)
    {
        var settings = new AzureCommunicationEmailSettings();
        configuration.GetSection(DefaultConfigSectionName).Bind(settings);
        configuration.GetSection($"{DefaultConfigSectionName}:{connectionName}").Bind(settings);

        var connectionString = configuration.GetConnectionString(connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            ParseConnectionString(connectionString, settings);
        }

        configureSettings?.Invoke(settings);
        Validate(settings, connectionName);
        return settings;
    }

    internal static void ParseConnectionString(
        string connectionString,
        AzureCommunicationEmailSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(settings);

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out _))
        {
            settings.Endpoint = connectionString;
            return;
        }

        foreach (var segment in connectionString.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException(
                    "The email connection string must contain key=value segments.");
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();

            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                settings.Endpoint = value;
            }
            else if (key.Equals("SenderAddress", StringComparison.OrdinalIgnoreCase))
            {
                settings.SenderAddress = value;
            }
            else if (key.Equals("AccessKey", StringComparison.OrdinalIgnoreCase))
            {
                settings.AccessKey = value;
            }
        }
    }

    private static void Validate(
        AzureCommunicationEmailSettings settings,
        string connectionName)
    {
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint)
            || !(endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                 || endpoint.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"A valid HTTP or HTTPS endpoint was not provided for email connection '{connectionName}'.");
        }

        if (string.IsNullOrWhiteSpace(settings.SenderAddress)
            || !System.Net.Mail.MailAddress.TryCreate(settings.SenderAddress, out _))
        {
            throw new InvalidOperationException(
                $"A valid SenderAddress was not provided for email connection '{connectionName}'.");
        }

        if (!string.IsNullOrWhiteSpace(settings.AccessKey))
        {
            try
            {
                _ = Convert.FromBase64String(settings.AccessKey);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    $"AccessKey for email connection '{connectionName}' must be a valid Base64 value.",
                    exception);
            }
        }
    }

    private static EmailClient CreateEmailClient(AzureCommunicationEmailSettings settings)
    {
        var endpoint = new Uri(settings.Endpoint!, UriKind.Absolute);
        if (!string.IsNullOrWhiteSpace(settings.AccessKey))
        {
            return new EmailClient(
                endpoint,
                new AzureKeyCredential(settings.AccessKey),
                settings.ClientOptions);
        }

        return new EmailClient(
            endpoint,
            settings.Credential ?? new DefaultAzureCredential(),
            settings.ClientOptions);
    }
}
