# Custom Domains

Azure Communication Services Email can send from an Azure-managed domain or from a domain
you own. This integration supports both, but it does not pretend that DNS verification is
instant or fully declarative. Azure has to verify records that live outside the Azure
resource graph, so a branded sender is a two-phase workflow.

## The Default: Azure-Managed Domain

Most apps can start with the default:

```csharp
using EmBrito.Aspire.Hosting;

var email = builder.AddAzureCommunicationEmail("email");
```

The generated Azure infrastructure includes an Email Communication Service, an
Azure-managed domain, and a Communication Services resource linked to that managed domain.
The sender address is usable after deployment and does not require DNS work.

This default is deliberately boring. It lets teams deploy and test the Azure path before
they decide whether a branded sender is worth the extra operational work.

## Branded Sender Domain

Declare the custom domain first:

```csharp
var email = builder.AddAzureCommunicationEmail("email");
var domain = email.AddCustomDomain("mail.contoso.com");
```

The first deployment creates the custom domain as a child of the same Email Communication
Service. It does not link the domain yet. The managed domain remains linked, so the
resource can still send email while the custom domain is pending.

After deployment, complete verification in Azure and at your DNS provider:

1. Add the ownership TXT record.
2. Ask Azure to verify domain ownership.
3. Add the SPF TXT record.
4. Add the DKIM and DKIM2 CNAME records.
5. Wait until Azure reports Domain, SPF, DKIM, and DKIM2 as verified.

Then declare the link in the AppHost:

```csharp
email.WithVerifiedDomain(domain, "notifications@mail.contoso.com");
```

Deploy again. That second deployment updates the Communication Services resource so it
links to the verified custom domain.

## Why This Is Two Steps

The custom domain resource is Azure infrastructure. The DNS records that prove ownership
are not. They live wherever the domain's authoritative DNS is hosted.

That means the first deployment can create the Azure-side domain resource, but Azure still
needs to observe DNS changes and complete asynchronous verification before the domain can
be linked for sending. The second deployment is the point where your AppHost catches up
with reality: verification has happened, so the desired linked domain can now be declared.

This is still infrastructure as code where it matters: resource ownership and the durable
domain link are expressed in the AppHost. The verification act itself is an external
control-plane and DNS process.

## Opt Out Of The Managed Domain

You can suppress the managed domain:

```csharp
var email = builder
    .AddAzureCommunicationEmail("email")
    .WithoutAzureManagedDomain();

var domain = email.AddCustomDomain("mail.contoso.com");
```

Use this only when it is acceptable for the Azure resource not to send email until the
custom domain is verified and linked. Without the managed domain, there is no temporary
sending domain during phase 1.

This opt-out affects what the integration emits from the current AppHost model. A normal
incremental Azure deployment will not automatically delete a managed domain that was
created by an earlier deployment. Removing already-created Azure resources is a separate
cleanup decision.

## Existing Verified Domains

If another team, repository, or deployment stack owns the Email Communication Service and
verified domain, link that domain by resource ID:

```csharp
email.WithExistingVerifiedDomain(
    "/subscriptions/.../resourceGroups/rg/providers/Microsoft.Communication/" +
    "emailServices/shared-email/domains/mail.contoso.com",
    "notifications@mail.contoso.com");
```

In this mode, this integration creates the Communication Services resource and points it
at the external verified domain. It does not create another Email Communication Service or
another copy of the domain.

## Redeployment Behavior

Repeated deployments preserve Azure's verification state. Redeploying the domain
declaration does not reset DNS records or start verification over.

The active link is managed by the AppHost. If someone manually changes the linked domain
in Azure Portal, a later deployment can replace that change with the domain declared in
code. Complete verification in Azure, then use `WithVerifiedDomain(...)` for the lasting
configuration.
