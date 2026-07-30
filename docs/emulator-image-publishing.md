# Emulator Image Publishing

## Why the emulator is a container image

Aspire emulators are normally distributed as OCI container images. First-party Aspire
integrations use the same model for services such as Azure App Configuration, Azure
Service Bus, and Azurite: the hosting NuGet package describes the resource and references
a versioned image, while a container runtime pulls and starts that image.

An OCI registry is therefore the container equivalent of a NuGet feed. Microsoft
first-party images commonly live in Microsoft Container Registry. This community
integration uses GitHub Container Registry (GHCR):

```text
ghcr.io/emerbrito/aspire-acs-email-emulator
```

GHCR is not inherently private. The released emulator package should be public so
consumers can pull it anonymously. A newly created GHCR package is private by default,
so its visibility must be changed to public after the first publication.

The NuGet package does not embed the image. Containers include an operating-system layer,
runtime, application, and architecture-specific artifacts, and OCI registries provide
the distribution, caching, manifests, and integrity model designed for them.

## Repository release workflow

`.github/workflows/emulator-image.yml` is intentionally manual. It has no push,
pull-request, or scheduled trigger.

The workflow accepts one decision: `publish` is `false` to build, test, smoke-test, and
scan without publishing, or `true` to perform those checks and publish the multi-platform
image. The version is never entered in the workflow form.

`src/Directory.Build.props` is the single version source for both NuGet packages and the
emulator image. The hosting assembly derives its pinned image tag from that package
version, and the workflow evaluates the same MSBuild version before building. The publish
job fails if that GHCR version already exists, preventing an immutable tag from being
overwritten.

Publishing is restricted to the repository's default branch and passes through the
`container-release` GitHub Environment. Configure that environment with required
reviewers if an explicit approval is desired. The workflow:

1. restores, formats, builds, tests, and packs both NuGet packages;
2. builds and smoke-tests the emulator container;
3. scans for unfixed high and critical vulnerabilities;
4. builds Linux AMD64 and ARM64 images;
5. creates an SBOM and provenance;
6. publishes immutable version and commit-SHA tags; and
7. pulls the published image to verify it is retrievable.

It intentionally does not publish a floating `latest` tag. Publish the emulator image
before publishing any NuGet version that references it.

To run it after the workflow exists on the default branch:

1. Open **Actions** > **Emulator container image** > **Run workflow**.
2. Select the default branch.
3. Leave `publish` disabled for a validation-only run.
4. Enable `publish` only for the release run.

The built-in `GITHUB_TOKEN` supplies the package credential; no long-lived registry
secret is required.

## What can remain private

In a public GitHub repository, the workflow YAML and workflow run history/logs are public.
They cannot be hidden while remaining in that repository. Only collaborators with write
access can manually dispatch this workflow, and repository/environment secrets remain
private and are masked in logs.

If the implementation of a pipeline itself must be confidential, it must live in a
separate private automation repository or external CI system. That extra boundary is not
needed here: the workflow contains no secret logic, is easier for consumers to audit, and
remains manually controlled.

## Removing the repository or image

The GHCR package is owned by the `emerbrito` account and can be associated with this
repository, but it remains a separately managed package. Do not rely on deleting the
repository to delete its container images.

The safest retirement order is:

1. Open the package from the `emerbrito` profile's **Packages** tab.
2. Delete individual versions or choose **Package settings** > **Danger Zone** >
   **Delete this package**.
3. Delete the repository afterward, if desired.

GitHub permits administrators to delete private packages. Public package versions can be
deleted directly while they have no more than 5,000 downloads; beyond that threshold,
GitHub Support must perform the deletion. A deleted package can normally be restored for
30 days if its namespace has not been reused.

Deleting an image referenced by a released hosting NuGet package breaks future emulator
pulls for that package version. Retain published images while those NuGet versions remain
supported, or clearly retire the corresponding packages together.

## Visual Studio F5: local source or published image

The committed sample always defaults to building the emulator from its local Dockerfile.
Nothing needs to be configured for normal source development.

To make F5 pull the published image instead, store this setting in the AppHost's user
secrets:

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

User secrets are outside the repository and cannot be committed accidentally. Published
image mode uses Aspire's `Always` pull policy, so F5 asks the registry for the immutable
tag pinned by the hosting package rather than silently using a stale local copy.

Use the release pipeline for the complete registry test:

1. Push the workflow to the default branch, which may still be in a private repository.
2. Run it once with `publish` disabled to validate the build.
3. Run it with `publish` enabled to publish the pinned preview version.
4. Change the GHCR package visibility to **Public** after its first publication.
5. Enable `EmailEmulator:UsePublishedImage` in user secrets and run F5.

If the preview fails verification, fix the issue and publish a new preview version. Update
the version once in `src/Directory.Build.props`; do not overwrite an already published
version.

## Choosing and bumping the version

The current shared properties are:

```xml
<VersionPrefix>0.1.0</VersionPrefix>
<VersionSuffix>preview.1</VersionSuffix>
```

They produce NuGet packages and an emulator image tagged `0.1.0-preview.1`. For another
preview, change only `VersionSuffix`, for example to `preview.2`. For the first stable
release, remove `VersionSuffix` and retain `VersionPrefix` as `0.1.0` only after every
direct NuGet dependency is also stable.

The hosting package currently depends on
`Azure.Provisioning.Communication` `1.0.0-beta.3`. While that dependency remains
prerelease, the integration must also retain a prerelease suffix. The workflow packs both
NuGet projects before publishing an image, and the repository treats NuGet warning
`NU5104` as an error, so an accidental stable release fails before anything reaches GHCR.

After a stable release:

- use `0.1.1` for a backward-compatible bug fix;
- use `0.2.0` for the next feature release while the integration remains below 1.0; and
- use a prerelease suffix such as `preview.1` when a version needs public validation
  before becoming stable.

The checked-in version declares what the next workflow run will publish. GHCR records
versions already published, and the workflow refuses to reuse one. Create a matching Git
tag such as `v0.1.0-preview.1` only after the image and NuGet packages for that version
have all succeeded.

Return to the source-built default by removing the setting:

```powershell
dotnet user-secrets remove `
  "EmailEmulator:UsePublishedImage" `
  --project src/samples/CommunicationEmail.AppHost
```
