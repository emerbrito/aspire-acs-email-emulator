# Design And Architecture

This document explains how the integration fits together and why the public API is shaped
the way it is. For step-by-step usage, start with [getting started](getting-started.md).
For custom-domain workflows, see [custom domains](custom-domains.md).

## Goals

The integration must make local email development safe and observable while producing a
real, identity-authenticated Azure Communication Services Email deployment in publish
mode. Its fluent API follows current Aspire Azure integrations: one logical resource can
be switched to an emulator, exposes expressions instead of prematurely resolved values,
supports `WithReference` and `WaitFor`, and assigns Azure roles to referencing compute.

## Resource model

`AddAzureCommunicationEmail("email")` creates one logical
`AzureCommunicationEmailResource`.

In Azure, its generated infrastructure contains:

1. `Microsoft.Communication/emailServices`
2. an `AzureManagedDomain` child with engagement tracking disabled by default
3. any customer-managed domain children declared with `AddCustomDomain(...)`
4. `Microsoft.Communication/communicationServices` linked to one verified domain
5. outputs for endpoint, sender address, resource name, and resource ID

An Azure-managed domain is the safe default because Azure preconfigures its sender
authentication and it is usable without a separate DNS-verification workflow. Custom
domains are an optional capability, not a prerequisite for using the integration.

The Email Communication Service owns its domain children. Communication Services is a
separate resource that links to one verified domain by resource ID; it does not own or
verify the domain. `AddCustomDomain(...)` therefore adds a child to the Email Communication
Service already owned by the logical Aspire resource. It never creates a second parent.

Applications that already have a verified domain managed by another infrastructure
boundary can link its full resource ID with `WithExistingVerifiedDomain(...)`. In that
mode this integration creates Communication Services but does not create a second Email
Communication Service or domain.

The service endpoint contains no access key. Referencing compute receives the
`Communication and Email Service Owner` role and the client uses Microsoft Entra
authentication through `DefaultAzureCredential`. Applications can supply another
`TokenCredential` in client settings.

The Azure provisioning implementation currently depends on
`Azure.Provisioning.Communication` 1.0.0-beta.3 because Microsoft has not published a
stable version of that provisioning library. This dependency is isolated to the hosting
package.

## Domain workflows

### Default: no domain work

```csharp
using EmBrito.Aspire.Hosting;

var email = builder.AddAzureCommunicationEmail("email");
```

This creates and links `AzureManagedDomain`. It is preverified, requires no DNS changes,
and provides the `donotreply` sender. This is the recommended path unless the application
requires a branded domain.

### Customer-managed domain: two deployments

Azure must prove that the consumer controls the DNS zone and must validate its sender
authentication records. ARM/Bicep can declare the domain resource, but domain verification
is a set of Azure control-plane actions combined with changes at the consumer's DNS
provider. Those operations cannot honestly be represented as one atomic declarative
deployment.

The integration makes that boundary explicit.

Phase 1 declares the custom domain:

```csharp
using EmBrito.Aspire.Hosting;

var email = builder.AddAzureCommunicationEmail("email");
var domain = email.AddCustomDomain("mail.contoso.com");
```

The generated infrastructure contains exactly one Email Communication Service, its
managed-domain child, the new `CustomerManaged` child, and one Communication Services
resource. The custom domain is intentionally not linked. The managed domain remains the
active sender so the application can continue sending during verification.

After deployment, the consumer uses Azure Portal or Azure CLI to:

1. copy the ownership TXT record to the authoritative DNS provider;
2. initiate and complete Domain verification;
3. add the required SPF TXT, DKIM CNAME, and DKIM2 CNAME records; and
4. wait until Azure reports Domain, SPF, DKIM, and DKIM2 as `Verified`.

Phase 2 declares the link:

```csharp
email.WithVerifiedDomain(domain, "notifications@mail.contoso.com");
```

`WithVerifiedDomain(...)` does not perform verification and does not bypass Azure. It is
an infrastructure assertion: the consumer states that verification is complete, and the
generated `linkedDomains` property switches to that custom domain. Azure rejects the
deployment if it cannot link the domain.

The custom domain declaration must remain in the AppHost in both phases. The second
deployment updates the link; it does not create a new domain or parent resource.

### Opting out of the managed domain

Consumers that do not want the default managed domain can opt out explicitly:

```csharp
using EmBrito.Aspire.Hosting;

var email = builder
    .AddAzureCommunicationEmail("email")
    .WithoutAzureManagedDomain();

var domain = email.AddCustomDomain("mail.contoso.com");
```

The first deployment creates the Email Communication Service, the pending custom domain,
and an unlinked Communication Services resource. It cannot send email until verification
is complete and `WithVerifiedDomain(...)` is added. The emitted sender address is only a
syntactically valid staging value during that interval.

This method suppresses the managed-domain declaration and link in generated
infrastructure. If a managed domain was deployed previously, a normal incremental ARM
deployment does not delete an omitted resource. Once a verified custom domain is selected,
the managed domain is no longer linked, but deleting the old child requires an explicit
cleanup operation or a deployment mechanism with managed deletion semantics, such as an
appropriately configured deployment stack.

### Reusing an externally managed verified domain

```csharp
email.WithExistingVerifiedDomain(
    "/subscriptions/.../providers/Microsoft.Communication/" +
    "emailServices/shared-email/domains/mail.contoso.com",
    "notifications@mail.contoso.com");
```

This is appropriate when another project, platform team, or deployment stack owns the
Email Communication Service and verified domain. The integration treats that domain as an
external dependency and links the Communication Services resource to it.

## What The AppHost Owns

The integration treats resource ownership and the selected sending domain as AppHost
state:

- the Email Communication Service, owned custom domains, Communication Services resource,
  and active `linkedDomains` value are generated from the AppHost;
- repeated deployments preserve the same resource identities and reapply the declared
  link;
- Azure-maintained verification status and generated verification records are read-only
  service state and are not reset by redeploying the domain declaration; and
- DNS records remain owned by the consumer's DNS infrastructure or registrar.

Verification initiation and DNS propagation are deliberately outside the generated Azure
deployment. This is not an accidental manual gap: Azure must observe proof held outside
the Azure resource graph, and verification completes asynchronously.

Consumers should not make a lasting link manually in Azure Portal. Because
`linkedDomains` is a property managed by this integration, a later deployment reapplies
the AppHost's declared value and can replace portal drift. Complete the external
verification manually, then express the durable link with `WithVerifiedDomain(...)`.

Likewise, incremental deployment does not delete resources merely because their
declaration was removed. `WithoutAzureManagedDomain()` guarantees that the current model
does not emit or link the managed domain; it cannot promise deletion of one created by an
earlier deployment.

## Emulator

The official Azure `EmailClient` sends HTTPS requests to the ACS Email REST API and uses
access-key or Microsoft Entra authentication. The local emulator implements the subset of
that REST contract used by `Azure.Communication.Email` 1.1.0:

- `POST /emails:send` accepts the Azure SDK payload and returns an operation;
- `GET /emails/operations/{id}` completes SDK polling with `Succeeded`;
- `Operation-Id` provides idempotency and rejects conflicting payload reuse;
- the current stable API version plus known compatible versions are allowlisted; and
- authentication headers are accepted but intentionally not validated locally.

`RunAsEmulator()` applies the surrogate-container pattern used by Aspire Azure
integrations and exposes one HTTP endpoint on container port 8080. That endpoint serves
the emulated ACS API, `/livez`, and the **Azure Communication Services Email Emulator**
web inbox.

The inbox supports search, message details, headers, To/CC/BCC and reply-to recipients,
text and HTML bodies, attachment downloads, and single or bulk deletion. A Server-Sent
Events endpoint pushes capture and deletion notifications to every connected browser.
The UI refreshes its current filtered list automatically without polling or replacing the
message being read.

Messages and their original JSON request are stored in SQLite. Message deletion is a
soft delete from the inbox: operation polling remains valid so deleting a message cannot
break an in-flight SDK call. HTML bodies render in a sandboxed iframe with a restrictive
content security policy; untrusted message fields are HTML-encoded. The same isolated
HTML endpoint is available through an `Open full size` action that opens a separate tab,
giving responsive email designs the full browser viewport without removing the inline
plain-text and HTML comparison.

The published container reference is pinned rather than using `latest`. Repository
samples override it with `WithDockerfile(...)`, allowing the image to be built from local
source during emulator development. NuGet can define and configure the container
resource, but normal consumers still need a retrievable container image because NuGet
packages do not carry a runnable OCI image or Docker daemon build context.

## Application integration

`AddAzureCommunicationEmailClient("email")` reads `ConnectionStrings:email`, whose format
is:

```text
Endpoint=<http-or-https-uri>;[AccessKey=<base64-key>;]SenderAddress=<verified-address>
```

The method is deliberately limited to configuration and dependency-injection
convenience. It validates the connection settings, chooses the appropriate constructor,
and registers the exact `Azure.Communication.Email.EmailClient` type from Microsoft's
NuGet package. The integration does not wrap, proxy, subclass, replace, or intercept that
client.

Local mode supplies a fixed, non-secret Base64 placeholder key. The integration constructs
Microsoft's `EmailClient` with `AzureKeyCredential`; the SDK generates its normal HMAC
authorization header and the emulator ignores authentication. Azure mode emits no key
and constructs the same client with the configured `TokenCredential`, or
`DefaultAzureCredential` when none is supplied.

Consumers inject the concrete `EmailClient`. The companion
`AzureCommunicationEmailSettings` exposes the resource-selected sender address and client
options. Keyed registrations are available through
`AddKeyedAzureCommunicationEmailClient(...)`.

No custom transport-facing sender wrapper is used. Application code compiles against and
injects Microsoft's client directly. Microsoft continues to own message serialization,
operation polling, retry behavior, API version selection, and Azure SDK diagnostics in
local and production execution.

## Fluent API

```csharp
using EmBrito.Aspire.Hosting;

var email = builder
    .AddAzureCommunicationEmail("email")
    .WithDataLocation("Europe")
    .WithUserEngagementTracking(enabled: false)
    .RunAsEmulator(emulator =>
    {
        emulator
            .WithDataVolume()
            .WithHostPort(18080);
    });
```

In publish mode `RunAsEmulator()` is a no-op, so the same AppHost deploys Azure resources.
Users can call `ConfigureInfrastructure(...)` from `Aspire.Hosting.Azure` for advanced
provisioning changes. Emulator storage is ephemeral by default. `WithDataVolume()` opts
into Docker-managed persistence, while `WithDataBindMount(path)` makes the SQLite data
host-visible.

## Security and operational choices

- No Azure access keys are generated or injected.
- The local access key is a fixed placeholder, not an Azure secret.
- Role assignment is scoped to the ACS resource and can be overridden per reference.
- Engagement tracking is disabled by default.
- Custom domains are never linked before the consumer explicitly calls
  `WithVerifiedDomain(...)`.
- The managed domain can be suppressed explicitly, but the default keeps email usable
  while a custom domain is being verified.
- The emulator is for local execution only and is never emitted into the publish model.
- `/livez` checks the emulator process without sending a billable or user-visible email.
- The emulator does not validate authentication and must not be exposed as a production
  service or treated as a security boundary.
- Captured messages can contain sensitive data; persistence is explicit, and consumers
  own retention and access to bind-mounted or Docker-volume data.

## Sources

- [Aspire source: Azure resource and emulator patterns](https://github.com/microsoft/aspire)
- [Aspire.Hosting.Azure 13.4.6](https://www.nuget.org/packages/Aspire.Hosting.Azure/13.4.6)
- [Azure Communication Services email domains](https://learn.microsoft.com/azure/communication-services/concepts/email/email-domain-and-sender-authentication)
- [Add and verify a custom email domain](https://learn.microsoft.com/azure/communication-services/quickstarts/email/add-custom-verified-domains)
- [Connect an email domain to ACS](https://learn.microsoft.com/azure/communication-services/quickstarts/email/connect-email-communication-resource)
- [Azure Resource Manager deployment modes](https://learn.microsoft.com/azure/azure-resource-manager/templates/deployment-modes)
- [Azure Communication Email client](https://www.nuget.org/packages/Azure.Communication.Email/1.1.0)
