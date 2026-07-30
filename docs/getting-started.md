# Getting Started

This integration has two NuGet packages separating resource modeling from
application client registration.

## Install The Packages

In the AppHost project:

```powershell
dotnet add package EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email
```

In the application project:

```powershell
dotnet add package EmBrito.Aspire.Azure.CommunicationServices.Email
```

## Model Email In The AppHost

```csharp
using EmBrito.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var email = builder
    .AddAzureCommunicationEmail("email")
    .RunAsEmulator();

builder.AddProject<Projects.Api>("api")
    .WithReference(email)
    .WaitFor(email);

builder.Build().Run();
```

`AddAzureCommunicationEmail("email")` creates one logical Aspire resource for Azure
Communication Services Email. The resource can run locally as the emulator and publish as
Azure infrastructure.

`RunAsEmulator()` changes local execution only. It starts the Azure Communication Services
Email Emulator container and adds an `Email inbox` URL to the Aspire dashboard. Publish
mode does not include the emulator.

## Register The Azure SDK Client

In the application:

```csharp
using EmBrito.Aspire.Azure.CommunicationServices.Email;

builder.AddAzureCommunicationEmailClient("email");
```

This registers Microsoft's `Azure.Communication.Email.EmailClient` from the
`Azure.Communication.Email` package. The extension method reads the Aspire connection
string and chooses the right Azure SDK constructor:

- local emulator: `AzureKeyCredential` with a non-secret development key;
- Azure: `TokenCredential`, using `DefaultAzureCredential` unless another credential is
  supplied.

The integration does not provide a custom sender abstraction. Your app injects and uses
Microsoft's client directly.

## Send A Message

```csharp
using Azure;
using Azure.Communication.Email;
using EmBrito.Aspire.Azure.CommunicationServices.Email;

app.MapPost(
    "/welcome",
    async (
        EmailClient emailClient,
        AzureCommunicationEmailSettings settings,
        CancellationToken cancellationToken) =>
    {
        var content = new EmailContent("Welcome")
        {
            PlainText = "Welcome!",
            Html = "<strong>Welcome!</strong>"
        };

        var message = new EmailMessage(
            settings.SenderAddress,
            "developer@example.test",
            content);

        var operation = await emailClient.SendAsync(
            WaitUntil.Completed,
            message,
            cancellationToken);

        return Results.Ok(new { operation.Id, operation.Value.Status });
    });
```

When the AppHost runs locally, the SDK posts to the emulator. When the AppHost is
published to Azure, the SDK posts to Azure Communication Services Email.

## Inspect Local Email

Run the AppHost and open the Aspire dashboard:
Open the `Email inbox` link on the email resource (which would be `email` in the above example).

The inbox shows messages as they arrive. The list stays visible on the left while the
selected message opens on the right, similar to a traditional email client. The HTML body
is rendered in a sandboxed frame and can be opened full size in a new browser tab.

Messages are ephemeral by default. Add persistence when you want captured email to survive
container restarts:

```csharp
.RunAsEmulator(emulator => emulator.WithDataVolume())
```

Use `WithDataBindMount(...)` when you want the SQLite store in a host-visible directory.

## Publish Behavior

The emulator exists for local development and testing only. In publish mode, the AppHost
resource produces Azure infrastructure for Email Communication Services, a Communication
Services resource, a sending domain, and the connection information consumed by your app.

The default deployment uses an Azure-managed sending domain. See
[custom domains](custom-domains.md) when you need a branded sender.
