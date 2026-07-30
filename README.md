# Azure Communication Services Email Emulator

An Aspire integration for Azure Communication Services Email, with a local emulator that
supports Microsoft's Azure Email SDK.

## Why Use It

- Develop against the real `Azure.Communication.Email.EmailClient`.
- Capture and inspect email in a browser inbox. Messages never leave the development
  environment.
- Keep email in the Aspire app model with `WithReference(...)`, `WaitFor(...)`, dashboard
  links, health checks, and publish-time Azure provisioning.

## Install

Add the hosting package to your AppHost:

```powershell
dotnet add package EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email
```

Add the client package to the application that sends email:

```powershell
dotnet add package EmBrito.Aspire.Azure.CommunicationServices.Email
```

The packages currently target .NET 10 and Aspire 13.

## AppHost

Model Azure Communication Services Email once:

```csharp
using EmBrito.Aspire.Hosting;

var email = builder
    .AddAzureCommunicationEmail("email")
    .RunAsEmulator();

builder.AddProject<Projects.Api>("api")
    .WithReference(email)
    .WaitFor(email);
```

During local development, `RunAsEmulator()` starts the Azure Communication Services Email
Emulator. The emulator accepts the REST requests produced by Microsoft's Azure Email SDK
and stores the messages locally. In the Aspire dashboard, open the `Email inbox` link on
the `email` resource to inspect captured email.

As expected for an Aspire integration, the emulator is never deployed. The same resource
becomes Azure Communication Services Email infrastructure when the app is published.

## Application Code

Register the Azure SDK client from Aspire configuration:

```csharp
using EmBrito.Aspire.Azure.CommunicationServices.Email;

builder.AddAzureCommunicationEmailClient("email");
```

This method is convenience registration only. It registers Microsoft's concrete
`Azure.Communication.Email.EmailClient` from the `Azure.Communication.Email` package. It
does not wrap, proxy, subclass, or replace the client.

Inject the client and send email the usual Azure SDK way:

```csharp
using Azure;
using Azure.Communication.Email;
using EmBrito.Aspire.Azure.CommunicationServices.Email;

public sealed class WelcomeMailer(
    EmailClient emailClient,
    AzureCommunicationEmailSettings emailSettings)
{
    public async Task SendAsync(string recipient, CancellationToken cancellationToken)
    {
        var content = new EmailContent("Welcome")
        {
            PlainText = "Welcome!",
            Html = "<strong>Welcome!</strong>"
        };

        var message = new EmailMessage(
            emailSettings.SenderAddress,
            recipient,
            content);

        await emailClient.SendAsync(
            WaitUntil.Completed,
            message,
            cancellationToken);
    }
}
```

Locally, the SDK sends to the emulator. In Azure, the same SDK sends to Azure
Communication Services Email.

## Custom Domains

You do not need a custom domain to start. By default, the integration provisions Azure's
managed domain so the resource has a usable sender after deployment.

If you need a branded sender, Azure requires DNS ownership and sender-authentication
verification. That process is naturally two steps because Azure has to see records at
your DNS provider before it can mark the domain as verified.

Declare the custom domain first, then publish the application to Azure:

```csharp
var email = builder.AddAzureCommunicationEmail("email");
var domain = email.AddCustomDomain("mail.contoso.com");
```

At this point, complete the required email-domain verification steps in Azure and DNS.
After Azure reports Domain, SPF, DKIM, and DKIM2 as verified, the domain can be linked.

## Link The Verified Domain

If you want Aspire to own the durable link, declare it in the AppHost and publish again:

```csharp
email.WithVerifiedDomain(domain, "notifications@mail.contoso.com");
```

This is naturally a two-step process: the first deployment creates the domain resource,
and the second deployment links it after Azure verification succeeds.

## Remove The Microsoft-Managed Domain

The managed domain stays available during verification unless you explicitly opt out with
`WithoutAzureManagedDomain()`.

## Documentation

- [Getting started](docs/getting-started.md)
- [Custom domains](docs/custom-domains.md)
- [Design and architecture](docs/design.md)
- [Maintainer guide](docs/maintenance.md)
- [Verification record](docs/verification.md)

The Visual Studio solution, samples, tests, and package projects live under
[`src`](src/README.md).
