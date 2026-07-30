# ACS Email REST Emulator Implementation Plan

## Objective

Replace the SMTP/Mailpit development path with a focused local implementation of the
Azure Communication Services Email data-plane API. Applications must use Microsoft's
unmodified `Azure.Communication.Email.EmailClient` and `EmailMessage` types in both local
and Azure environments.

The emulator is a local-development resource only. `RunAsEmulator()` remains a no-op in
publish mode, where the integration provisions and references real Azure Communication
Services resources.

## Confirmed feasibility

The repository's `Azure.Communication.Email` 1.1.0 client was tested against a local HTTP
listener using this connection-string shape:

```text
Endpoint=http://127.0.0.1:<port>;AccessKey=<base64-development-key>;SenderAddress=donotreply@localhost
```

The official client:

1. accepted HTTP and the additional `SenderAddress` property;
2. generated its HMAC authorization header locally without contacting Azure;
3. posted the native JSON payload to `/emails:send?api-version=2025-09-01`;
4. accepted a `202` response containing a running operation;
5. polled `/emails/operations/{operationId}`; and
6. completed with `EmailSendStatus.Succeeded`.

The emulator may ignore the HMAC header, but it must never log or persist authentication
headers.

## Runtime architecture

One ASP.NET Core container serves three surfaces on one HTTP endpoint:

- the ACS-compatible data-plane API;
- a local inbox web UI; and
- an emulator administration API under `/_emulator`.

SQLite stores captured messages and operation state. The original JSON request is retained
for forward compatibility while normalized fields support the inbox and search.

The hosting package defines the resource, endpoint, health check, connection string, URL,
and optional persistence. The emulator executable is built in this repository and is
distributed as a versioned OCI image for released NuGet packages.

## Data-plane compatibility contract

Initial supported API versions:

- `2023-03-31`
- `2024-07-01-preview`
- `2025-09-01`

Required endpoints:

```text
POST /emails:send?api-version={version}
GET  /emails/operations/{operationId}?api-version={version}
```

The POST endpoint:

- validates the API version and required payload structure;
- uses the optional `Operation-Id` UUID header or creates a UUID;
- treats `Operation-Id` as an idempotency key;
- stores the request atomically;
- returns HTTP 202 with `{ "id": "...", "status": "Running" }`;
- returns an absolute `Operation-Location`; and
- never sends real email.

The GET endpoint returns HTTP 200 and a terminal `Succeeded` result for a captured message.
The operation record is independent of inbox deletion so SDK polling remains stable.

Azure acceptance is not delivery. The UI and documentation must say that the emulator
captures messages locally and does not model delivery, bounces, filtering, Event Grid,
quotas, throttling, DNS validation, domain authorization, or engagement tracking.

## Storage

SQLite is stored under `/data`. Default execution is ephemeral. The hosting API will add:

```csharp
emulator.WithDataVolume();
emulator.WithDataBindMount("./.email-emulator");
```

Each message stores:

- operation ID and capture timestamp;
- API version and client request ID;
- sender;
- To, CC, BCC, and reply-to recipients including display names;
- subject, plain text, and HTML;
- custom headers;
- attachment metadata and decoded bytes;
- engagement-tracking flag; and
- original request JSON.

The initial implementation enforces the ACS 10 MB request limit. Search covers operation
ID, sender, recipients, subject, and textual content.

## Inbox UI and security

The server-rendered UI provides:

- newest-first inbox;
- text search;
- message detail;
- To, CC, BCC, reply-to, and custom headers;
- plain-text and HTML body views;
- attachment downloads;
- delete-one and delete-all actions; and
- an explicit local-capture indicator.

All user-controlled values are HTML encoded. HTML email is rendered only in a sandboxed
iframe with a restrictive content security policy. Attachments are served with safe file
names, `X-Content-Type-Options: nosniff`, and attachment content disposition.

The emulator has no authentication and must remain local-only. Aspire exposes its host
endpoint for local access but never publishes it as an Azure resource.

### Email-client UI phase

The inbox evolves from separate list and detail pages into a traditional master-detail
email client:

- the official product-aligned title is **Azure Communication Services Email Emulator**;
- the message list remains visible in a left pane;
- the selected message renders in the right reading pane;
- selection updates the browser URL and history without a document navigation;
- direct and non-JavaScript links retain progressive-enhancement behavior;
- search and delete actions update the relevant pane without leaving the inbox;
- the sandboxed HTML body can open full size in a separate browser tab while the inline
  plain-text and HTML comparison remains unchanged;
- narrow screens stack the list and reading pane; and
- Server-Sent Events notify connected browsers when messages are captured or deleted.

Server-Sent Events are used instead of polling or WebSockets because emulator
notifications are one-way. The browser reconnects automatically, while the server keeps
no durable UI-event log. On any notification, the client refreshes the current filtered
list from a server-rendered fragment; it does not interrupt the message currently being
read.

## Client integration

Remove the SMTP transport, MailKit dependency, custom sender abstraction, custom send
result, and SMTP-specific telemetry/health check.

Retain the client NuGet package as a thin native-client integration:

```csharp
builder.AddAzureCommunicationEmailClient("email");
builder.AddKeyedAzureCommunicationEmailClient("email");
```

It registers Microsoft's concrete `EmailClient`.

- A connection string containing `AccessKey` selects the native HMAC constructor for the
  local emulator.
- An endpoint without `AccessKey` selects the native `TokenCredential` constructor with
  `DefaultAzureCredential` by default.

The settings object exposes `Endpoint`, `AccessKey`, `SenderAddress`, `Credential`, and
`EmailClientOptions`. No send wrapper or alternative email model is introduced.

This constructor selection preserves managed identity and RBAC in Azure without requiring
the application to branch between local and cloud execution.

## Aspire hosting behavior

Local connection string:

```text
Endpoint=http://{emulator-host}:{emulator-port};AccessKey={fixed-base64-development-key};SenderAddress=donotreply@localhost
```

Azure connection string:

```text
Endpoint={acs-endpoint};SenderAddress={verified-sender}
```

`RunAsEmulator()` configures:

- one HTTP endpoint;
- the versioned released container image;
- a dashboard URL labelled `Email inbox`;
- a live health check; and
- local-only emulator annotations.

The existing emulator-builder callback remains available. Repository samples override the
released image with a local Dockerfile:

```csharp
email.RunAsEmulator(emulator =>
{
    emulator.WithDockerfile(
        "../..",
        "EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/Dockerfile");
});
```

Aspire therefore builds and starts the emulator from source during development. No
registry publication is required before local and CI testing.

## Distribution

Released artifacts are:

1. `EmBrito.Aspire.Hosting.Azure.CommunicationServices.Email` NuGet package;
2. `EmBrito.Aspire.Azure.CommunicationServices.Email` client NuGet package; and
3. a pinned, multi-architecture OCI emulator image.

The release pipeline must build and test the exact image, generate an SBOM, scan it,
publish an immutable version tag, and only then publish NuGet packages that reference that
tag. Floating `latest` tags are not used by the integration.

The container workflow is implemented in `.github/workflows/emulator-image.yml` as a
manual-only workflow. It supports validation without publication and a default-branch
publication path through the `container-release` GitHub Environment. The sample keeps its
local Dockerfile build as the committed default and supports an always-pulled registry
image through untracked user secrets. The complete maintainer procedure is documented in
`emulator-image-publishing.md`.

## Verification

Automated coverage includes:

- POST and operation polling through the real Microsoft `EmailClient`;
- `WaitUntil.Started` and `WaitUntil.Completed`;
- generated, explicit, duplicate, and conflicting operation IDs;
- concurrent messages and concurrent retries with the same operation ID;
- all recipient groups and reply-to;
- plain text, HTML, headers, regular attachments, and inline attachments;
- payload and API-version validation;
- search and delete behavior;
- HTML isolation and attachment safety;
- persistence-related hosting annotations;
- hosting resource annotations and connection expressions;
- local HMAC versus Azure identity client registration;
- solution build, tests, formatting, and packaging;
- local Dockerfile build through the sample AppHost;
- Aspire health and resource readiness; and
- live HTTP inspection of the inbox and message detail surfaces.

## Implementation sequence

1. Create the emulator web project, persistence model, ACS endpoints, and contract tests.
2. Add the inbox UI, administration endpoints, and security tests.
3. Replace Mailpit annotations with the emulator HTTP resource and persistence APIs.
4. Simplify the client package to native `EmailClient` registration.
5. Update the sample API and AppHost to use the local Dockerfile build.
6. Remove SMTP dependencies and obsolete tests.
7. Update design, package, root, and verification documentation.
8. Run the complete validation matrix without pushing or merging the branch.

## Implementation status

Steps 1 through 8 of the REST emulator phase are complete and incorporated into the
local `main` branch. The email-client UI phase is complete on
`codex/emulator-email-client-ui`, including the responsive master-detail interface and
Server-Sent Event updates. Automated and browser validation are recorded in
`verification.md`. The AppHost and containers are stopped after validation.

The manual release workflow, multi-architecture build, SBOM, provenance, vulnerability
scan, published-image F5 switch, and maintainer documentation are implemented on
`codex/emulator-image-publishing`.

Still intentionally outside this development branch:

- publishing an emulator image or NuGet package;
- the maintainer's pipeline-published GHCR and Visual Studio F5 verification;
- merging to `main`; and
- pushing any repository branch.
