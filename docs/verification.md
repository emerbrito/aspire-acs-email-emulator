# Verification record

Automated verification completed on July 29, 2026 with:

- .NET SDK 10.0.300 (`net10.0`, C# 14);
- Aspire CLI and packages 13.4.6; and
- Docker Desktop using the .NET 10 SDK and ASP.NET runtime container images.

The maintainer's Visual Studio F5 verification of the source-built emulator was completed
before merge. Pulling the GHCR-published emulator image through Visual Studio F5 was also
verified after the preview image was published.

## Automated checks

- Fresh solution restore: succeeded.
- Full solution build: succeeded with zero warnings and zero errors.
- `dotnet format --verify-no-changes`: succeeded.
- Client integration tests: 10 passed.
- Hosting integration tests: 23 passed.
- Emulator contract, concurrency, and UI tests: 12 passed.
- Full solution test total: 45 passed.
- NuGet vulnerability audit, including transitive dependencies: no known vulnerable
  packages reported.
- Both NuGet packages built successfully in Release configuration.
- The manual GitHub Actions container workflow passed `actionlint` 1.7.12.
- Both NuGet packages evaluate and pack as `0.1.0-preview.1` from the shared
  `Directory.Build.props` version, and the emulator image annotation derives the same tag
  from the hosting assembly.
- A simulated stable `0.1.0` hosting-package pack fails with `NU5104` while
  `Azure.Provisioning.Communication` remains `1.0.0-beta.3`; the image workflow now packs
  both NuGet packages before any publish job can run.
- The workflow derives that evaluated package version without a form input and rejects a
  publish when the corresponding GHCR tag already exists.
- The workflow-equivalent Docker build and live health/UI smoke test succeeded.
- Aspire resolved the sample's default mode to a locally built `email:<content-hash>`
  image and reported the resource healthy.
- With user-secret registry settings, Aspire resolved the resource to the pinned
  `ghcr.io/emerbrito/aspire-acs-email-emulator:0.1.0-preview.1` image
  and the maintainer verified the sample through Visual Studio F5.

The emulator tests use Microsoft's `Azure.Communication.Email.EmailClient`, not a custom
HTTP test client, and cover:

- send plus long-running-operation polling;
- To, CC, BCC, reply-to, custom headers, and attachments;
- operation ID idempotency and conflict responses;
- parallel sends and concurrent retries;
- inbox deletion without invalidating an SDK operation;
- inbox rendering, master-detail fragments, and HTML content security policy;
- Server-Sent Event notification after a real SDK send;
- static UI assets for fragment navigation and live updates; and
- unsupported API-version responses.

## Live Aspire check

The sample AppHost was started through:

```powershell
aspire start `
  --apphost src/samples/CommunicationEmail.AppHost/CommunicationEmail.AppHost.csproj `
  --isolated `
  --non-interactive
```

The emulator container was built from the repository Dockerfile through
`WithDockerfile(...)`. Both `email` and `api` reached `Healthy` through `aspire wait`.

One feature-verification message was sent through the sample API. The API's injected
first-party `EmailClient` completed with:

```text
status: Succeeded
operation ID: 3ef0bade-5933-4df2-a7b1-e27dce539445
```

The emulator admin endpoint and web inbox contained one message with:

- sender: `donotreply@localhost`;
- recipient: `developer@example.test`;
- subject: `Aspire ACS REST emulator verification`; and
- both plain-text and HTML content.

The inbox list, message detail page, and HTML-body endpoint returned HTTP 200. The HTML
body response included the expected restrictive content security policy. Aspire was
stopped normally after verification, removing the isolated session container and
releasing build outputs.

The email-client UI phase was also exercised in Chromium at desktop (1440 x 900) and
mobile (390 x 844) viewport sizes. The live check confirmed:

- the exact visible and document title `Azure Communication Services Email Emulator`;
- a side-by-side inbox and reading pane on desktop;
- a stacked, fully labelled layout on mobile;
- two emails sent through the sample API and its injected Microsoft `EmailClient`
  appearing through Server-Sent Events without a manual refresh;
- a new message not stealing the current selection;
- in-place selection with browser history support and zero additional document loads;
- the inline 454-pixel HTML preview selecting the responsive email's narrow layout while
  `Open full size` opened the same sandboxed body at 1440 pixels in a separate tab and
  selected its desktop layout; and
- no browser console or page errors.

## Publish-model check

`aspire publish` completed locally for the AppHost's Azure Container Apps environment.
No cloud deployment was performed.

The generated Bicep contained:

- the Email Communication Service;
- the Azure-managed domain;
- the linked Communication Services resource;
- the `Communication and Email Service Owner` role assignment;
- the Azure endpoint and sender-address application connection string; and
- no emulator image, localhost endpoint, or local access key.
