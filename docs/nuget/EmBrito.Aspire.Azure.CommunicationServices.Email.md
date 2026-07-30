# Azure Communication Services Email Emulator Client For Aspire

Application package for the Azure Communication Services Email Emulator.

Use this package in the app that sends email. It reads the Aspire connection and registers
Microsoft's `Azure.Communication.Email.EmailClient`.

## Install

```powershell
dotnet add package EmBrito.Aspire.Azure.CommunicationServices.Email
```

## Register The Client

```csharp
using EmBrito.Aspire.Azure.CommunicationServices.Email;

builder.AddAzureCommunicationEmailClient("email");
```

The registered client is Microsoft's concrete `EmailClient`. This package does not wrap,
proxy, subclass, or replace the Azure SDK client.

## Send Email

```csharp
using Azure;
using Azure.Communication.Email;
using EmBrito.Aspire.Azure.CommunicationServices.Email;

var message = new EmailMessage(
    emailSettings.SenderAddress,
    "developer@example.test",
    new EmailContent("Hello") { PlainText = "Hello from Aspire." });

await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
```

Local Aspire runs send to the emulator inbox. Azure deployments send to Azure
Communication Services Email.

## AppHost Package

Add `EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email` to the AppHost to model the
email resource and run the emulator.

## More

See the repository for examples, custom-domain guidance, design notes, and maintainer
documentation:

https://github.com/emerbrito/aspire-acs-email-emulator
