# Azure Communication Services Email Emulator For Aspire

AppHost package for the Azure Communication Services Email Emulator.

Use this package when an Aspire application needs Azure Communication Services Email:
local runs start an emulator with a browser inbox, and publish mode creates the Azure
email resources.

## Install

```powershell
dotnet add package EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email
```

## AppHost

```csharp
using EmBrito.Aspire.Hosting;

var email = builder
    .AddAzureCommunicationEmail("email")
    .RunAsEmulator();

builder.AddProject<Projects.Api>("api")
    .WithReference(email)
    .WaitFor(email);
```

Open the `Email inbox` link in the Aspire dashboard to inspect captured messages. No real
email is sent by the emulator.

In Azure, the same AppHost resource publishes Azure Communication Services Email
infrastructure instead of the emulator.

## Application Package

Add `EmBrito.Aspire.Azure.CommunicationServices.Email` to the app that sends email. It
registers Microsoft's `Azure.Communication.Email.EmailClient` from the Aspire connection.

## More

See the repository for examples, custom-domain guidance, design notes, and maintainer
documentation:

https://github.com/emerbrito/aspire-acs-email-emulator
