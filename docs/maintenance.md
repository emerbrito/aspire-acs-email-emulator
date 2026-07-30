# Maintainer Guide

This document is for project maintainers. It covers the release mechanics that consumers
should not need to think about.

## Version Source

The version is controlled in `src/Directory.Build.props`:

```xml
<VersionPrefix>0.1.0</VersionPrefix>
<VersionSuffix>preview.1</VersionSuffix>
```

Those values produce:

- NuGet package version `0.1.0-preview.1`;
- emulator image tag `0.1.0-preview.1`; and
- a hosting package that points at the matching emulator image.

For the next preview, change only `VersionSuffix`, for example:

```xml
<VersionSuffix>preview.2</VersionSuffix>
```

For the first stable release, remove `VersionSuffix`. Do that only after every direct
NuGet dependency can support a stable package. The hosting package currently depends on
`Azure.Provisioning.Communication` `1.0.0-beta.3`, so the package must remain prerelease.
The build treats `NU5104` as an error to prevent an accidental stable package while a
direct dependency is still prerelease.

Published NuGet versions and container image tags are immutable for this project. If a
published preview has a problem, fix it and publish a new preview version.

## Emulator Image

The emulator is distributed as an OCI image:

```text
ghcr.io/emerbrito/aspire-acs-email-emulator
```

The hosting NuGet package references a pinned version tag. It does not use `latest`.

GitHub Container Registry package pages get their short public description from OCI image
annotations. Keep the workflow's `org.opencontainers.image.description` clear and
consumer-facing because it is effectively the image package's elevator pitch.

## Publish The Emulator Image

The workflow is `.github/workflows/emulator-image.yml`. It is manual-only.

Use the default branch when publishing:

1. Open **Actions** > **Emulator container image**.
2. Select **Run workflow**.
3. Leave `publish` disabled for a validation-only run.
4. Enable `publish` for the release run.
5. Approve the `container-release` environment if required.

The workflow restores, formats, builds, tests, packs both NuGet packages, builds and
smoke-tests the emulator image, scans it for high and critical vulnerabilities, then
publishes a Linux AMD64/ARM64 image.

When the repository is public, GitHub artifact attestation can persist provenance for the
image. BuildKit provenance and SBOM metadata are also attached to the OCI image.

After the first GHCR package exists, make the package public before publishing NuGet
packages that depend on it. A public GHCR package lets consumers pull the emulator without
authenticating to GitHub.

## Visual Studio F5 Image Selection

The committed sample defaults to building the emulator from source:

```csharp
email.RunAsEmulator(emulator =>
{
    emulator.WithDockerfile(
        "../..",
        "EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/Dockerfile");
});
```

That is the right default for repository development. It means F5 picks up local emulator
changes before an image is published.

To test the published image through F5 without changing tracked source, set an AppHost
user secret:

```powershell
dotnet user-secrets set `
  "EmailEmulator:UsePublishedImage" "true" `
  --project src/samples/CommunicationEmail.AppHost
```

Visual Studio's **Manage User Secrets** command can set the equivalent JSON:

```json
{
  "EmailEmulator": {
    "UsePublishedImage": true
  }
}
```

Published-image mode uses Aspire's `Always` pull policy, so Docker asks GHCR for the
immutable image tag pinned by the hosting package.

Return to source-built mode with:

```powershell
dotnet user-secrets remove `
  "EmailEmulator:UsePublishedImage" `
  --project src/samples/CommunicationEmail.AppHost
```

## NuGet Release Preparation

The workflow is `.github/workflows/nuget-packages.yml`. It is manual-only and mirrors
the image workflow: leave `publish` disabled to build, test, pack, and upload package
artifacts without publishing; enable `publish` to push the packages to NuGet.org.

Create a GitHub Environment named `nuget-release` before the first publish. Configure a
required reviewer if you want GitHub to pause before the publish job can request a
short-lived NuGet credential.

Set an environment variable named `NUGET_USER` on `nuget-release`. Its value is the
nuget.org username that owns the packages. This is not an API key.

On nuget.org, create a Trusted Publishing policy for the package owner:

- Repository Owner: `emerbrito`
- Repository: `aspire-acs-email-emulator`
- Workflow File: `nuget-packages.yml`
- Environment: `nuget-release`

Before publishing a NuGet version:

1. Publish the matching emulator image.
2. Confirm the GHCR package is public.
3. Run **NuGet packages** with `publish` disabled.
4. Test the downloaded `.nupkg` artifacts in a clean Aspire consumer app.
5. Run **NuGet packages** again with `publish` enabled.
6. Approve the `nuget-release` environment if required.
7. Create a matching Git tag, for example `v0.1.0-preview.1`.

The NuGet publish job refuses to run from a non-default branch, verifies that the matching
GHCR image tag exists, requests a short-lived credential through NuGet Trusted Publishing,
and then pushes the packages. Do not publish NuGet before the matching emulator image is
available.

## GHCR Cleanup

The GHCR package is managed separately from the
repository.
