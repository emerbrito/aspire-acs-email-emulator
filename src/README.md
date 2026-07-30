# Solution

This folder contains the Visual Studio solution for the EmBrito Aspire Azure
Communication Services Email integration.

Open `EmBrito.Aspire.Azure.CommunicationServices.Email.slnx` in Visual Studio or build it
from the command line:

```powershell
dotnet restore EmBrito.Aspire.Azure.CommunicationServices.Email.slnx
dotnet build EmBrito.Aspire.Azure.CommunicationServices.Email.slnx --no-restore
dotnet test EmBrito.Aspire.Azure.CommunicationServices.Email.slnx --no-build
```

Run the sample AppHost with Aspire:

```powershell
aspire start --apphost samples/CommunicationEmail.AppHost/CommunicationEmail.AppHost.csproj
```

The sample starts an API with a small "send email" form and an `email` resource with an
`Email inbox` dashboard link. Local email is captured by the emulator; no real email is
sent.

## Projects

- `EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email`: AppHost integration,
  emulator resource, and Azure provisioning.
- `EmBrito.Aspire.Azure.CommunicationServices.Email`: application-side registration for
  Microsoft's Azure `EmailClient`.
- `EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator`: local ACS Email REST
  emulator and inbox UI.
- `samples/CommunicationEmail.AppHost`: Aspire sample.
- `samples/CommunicationEmail.Api`: simple sender app used for manual testing.
- `tests`: hosting, client, and emulator tests.

The public documentation starts in [../docs/README.md](../docs/README.md).
