# Shoutkit Windows development

Both solution formats expose Debug/Release on ARM64 and x64 only. Native app/tests/performance projects map to that architecture; portable libraries map explicitly to Any CPU. Run `./scripts/Test-SolutionConfigurations.ps1 -SelfTest` with the pinned SDK to validate both formats against evaluated project configurations without restoring packages or launching UI. CI runs this check before restore. Reload the solution in Visual Studio after configuration mapping changes.

Portable radio libraries target stable .NET 10 alongside the Windows app's pinned .NET 11 RC. See [RADIO_SDK.md](RADIO_SDK.md) for extraction boundaries, headless tests, local package consumers and compatibility/trim checks. VS Code and Zed have a portable test task; the dev container installs both SDKs and runs portable tests after its RadioCore build.

This is the active Windows ARM64/x64 WinUI 3 preview. The solution is `smodr.slnx`,
and `global.json` pins .NET SDK `11.0.100-rc.1.26425.128`. See [README.md](README.md)
for the current app behavior and self-contained publish command.

## Windows host: VS Code or Zed

Use a Windows host with the pinned .NET 11 RC SDK and the Windows build tools
required by WinUI. Restore with the checked-in lock files before building. The
editor tasks use `scripts/dotnet-dev.ps1`, which looks for:

1. `SHOUTKIT_DOTNET_ARM64` or `SHOUTKIT_DOTNET_X64` (an absolute `dotnet.exe` path),
2. `SHOUTKIT_DOTNET`,
3. this workspace's optional sibling SDK folders (`../dotnet11-rc-sdk/runtime/`
   and `../dotnet11-rc-x64/runtime/`), or
4. a matching SDK on PATH for the host's architecture.

The resolver checks the version against `global.json` and fails with a useful
message rather than silently building with .NET 8 or 10. A globally installed
SDK may still be needed for the editor's C# language server; the resolver only
controls tasks. On Windows ARM64, use an x64 SDK/runtime for x64 test execution.

VS Code has ARM64 and x64 restore, build, and test tasks in `.vscode/tasks.json`.
The ARM64 build is the default build task. The launch configurations point at
the actual `net11.0-windows10.0.22621.0` Debug outputs. Because Shoutkit is
single-instance, choose **Attach to running Shoutkit** if it is already open;
launching a second process only activates the first window. These are Windows
debug configurations, not container launch configurations.

Zed has corresponding commands in `.zed/tasks.json`. Install Zed's C# extension
for Roslyn language services; `.zed/settings.json` keeps generated output out of
file scanning and formats changed C# lines on save. Zed's tasks build/test the
Windows app on the host, but do not provide a WinUI visual designer.

For a terminal build on this Windows ARM64 host:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet-dev.ps1 ARM64 restore smodr.slnx -p:Platform=ARM64 --locked-mode -p:NuGetAudit=false --ignore-failed-sources
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet-dev.ps1 ARM64 test smodr.slnx -c Release --no-restore -p:Platform=ARM64 -warnaserror
```

The local editor restore disables NuGet auditing because corporate networks may
block the advisory endpoint. Missing packages still fail the restore. CI uses
an ordinary audited restore and is the release gate.

## Linux dev container and Codespaces

Open the repository in VS Code Dev Containers or GitHub Codespaces. The
container installs .NET 10.0.401 and the exact SDK pinned in `global.json`.
Prebuild/update-content restores dependencies; post-create builds RadioCore and
RadioSmoke and runs portable tests with warnings as errors. It never restores
the Windows solution or launches live streams.

The container supports editing, C# navigation, documentation, and cross-platform
RadioCore builds. WinUI compilation, the app test project, native playback,
packaging, and interactive debugging still require a Windows host or Windows CI.
The container validation workflow also packs all four libraries, runs an offline
package consumer and publishes/runs a fully trimmed directory-client consumer.
Every failed restore, build, test or consumer fails the workflow. It never pushes
images. Existing Windows CI remains responsible for native WinUI.

```bash
bash scripts/validate-portable-radio.sh --quick --include-compat
# Full packages, offline consumers and architecture-native Linux trimming:
bash scripts/validate-portable-radio.sh --include-compat
```

If the container cannot download the pinned SDK or NuGet packages behind a
corporate proxy, fix network/proxy access or use the Windows host's cached SDK;
the setup intentionally does not hide that failure.

Named volumes preserve NuGet downloads and isolated build outputs between
container rebuilds. The portable Dockerfile restores dependencies before copying
source; BuildKit mounts retain packages. Source changes still invalidate test
layers. Removing caches causes a cold restore, not a change in correctness.
Local draft package versions are unique to avoid testing stale cached drafts.
Codespaces no longer requests additional cross-repository write permissions.

The Linux solution is `portable/Radio.Portable.slnx`, scoped to stable .NET 10.
Run `dotnet` from `portable/` to select that SDK. In C# Dev Kit, select this
solution if your root workspace setting overrides the container default with the
Windows solution. Windows ARM64/x64 tasks remain available on the host.

Set `RADIO_NUGET_SOURCE=https://www.nuget.org/api/v2/` when corporate policy blocks
v3. Local container restore disables advisory auditing; existing audited SDK CI
remains the security gate. Missing packages still fail, without an
`--ignore-failed-sources` workaround.

## Local Docker without disturbing the desktop

PowerShell 7.2 or newer is required:

```powershell
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -CheckOnly
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -Target tests
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -Target full
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -Target compat
pwsh -NoProfile -File scripts/Test-LocalDockerHarness.ps1
```

`tests` runs portable tests using stable .NET 10; `full` adds package/consumer and
trim checks on Linux ARM64/x64; `compat` builds RadioCore/RadioSmoke using the
pinned .NET 11 SDK and runs portable tests. Only local images are built. The
ten-second probe never starts, unpauses or changes Docker. An unavailable or
Windows-container engine fails by default. `-IfAvailable` explicitly reports a
skip, not a pass. `-NativeFallback` runs native portable tests when Docker is
unavailable and reports that Docker/packages/compatibility were not validated.
No task opens the app, plays audio or publishes packages.

Force fresh validation while retaining SDK/dependency downloads:

```powershell
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -Target full -ForceRetest
pwsh -NoProfile -File scripts/Test-LocalDocker.ps1 -Target compat -ForceRetest
pwsh -NoProfile -File scripts/Test-DevContainerLifecycle.ps1
```

`-ForceRetest` assigns a unique build argument only to validation layers rather
than disabling the entire Docker cache. Fresh-run identity is checked against
the exported image. Default builds can reuse previously validated layers.
Successful runs export TRX reports, image/run provenance and (for `full`) four
NuGet packages into unique folders under `out/docker-validation/`. `-OutputRoot`
selects another evidence root without replacing previous runs. Build failures
remain failures; a failed build cannot export its uncommitted layer, so consult
the build log. Export failures also fail validation rather than claiming success.

The lifecycle test uses a source snapshot and two fresh, uniquely named volumes,
then runs the real prewarm/post-create hooks twice as `vscode`, never as root.
It verifies cold/warm restores, writable cache mounts, tests, and the absence of
checkout-local `bin`/`obj` outputs. It exports both test reports and removes only
its own stopped container and disposable volumes. It neither touches the host
checkout nor clears an existing developer cache. This models container lifecycle
hooks locally; it does not create or verify a hosted Codespace or an editor session.

`.github/workflows/docker-validation.yml` runs the same full, compatibility and
non-root lifecycle checks on native Linux x64 (`ubuntu-24.04`) and ARM64
(`ubuntu-24.04-arm`) runners, with fresh validation and evidence upload. Runner
labels follow the [GitHub-hosted runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
This workflow complements the existing audited SDK and Windows gates; it never
publishes images or packages. Local ARM64 validation does not substitute for a
future x64 CI run after pushing/merging.

## Resolving the pinned Windows SDK

The root `global.json` keeps .NET 11 RC rather than retargeting the app to .NET 10.
Search paths include the invoking host, repo-local `.dotnet`, and this enlistment's
sibling ARM64 RC installation. On this host, ordinary `dotnet --version` resolves
`11.0.100-rc.1.26425.128`. Explicit x64 hosts retain their own SDK via host-first
search order. `scripts/dotnet-dev.ps1` remains available for older hosts;
`SHOUTKIT_DOTNET_ARM64`/`SHOUTKIT_DOTNET_X64` select explicit binaries.

For a new enlistment, install the exact SDK from `global.json` into `.dotnet`
using Microsoft's dotnet-install script or use an architecture override.
`.dotnet` is ignored by Git and excluded from Docker contexts. SDK search paths
require a .NET 10-or-newer host; see Microsoft's
[local SDK discovery guidance](https://learn.microsoft.com/en-us/dotnet/core/tools/test-prerelease-sdk-locally).
Visual Studio must also support the preview SDK/MSBuild version and have preview
SDK use enabled. CLI resolution does not establish support in an older IDE.
Reload the solution after changing SDK resolution.

### Local container verification (2026-10-02)

Verified through Docker Desktop's Linux ARM64 engine on the Windows host:

- `-Target full`: 156 portable tests passed, four packages produced, offline
  package consumer passed, and the self-contained fully trimmed Linux ARM64
  consumer passed. Running the resulting image with `--network none` also passed.
- `-Target compat`: RadioCore and RadioSmoke built with the pinned .NET 11 RC SDK
  with zero warnings/errors, and all 156 stable-runtime portable tests passed.
- An unchanged repeat of the full image build reused validation layers and
  completed in approximately 6.5 seconds on this host. That is cache reuse,
  not a fresh execution of the tests or a cross-machine performance promise.
- Fourteen Docker harness fixtures cover availability, safe arguments, fresh-run
  identity arguments, report-export failures, and Windows executable discovery
  when both `docker.exe` and an extensionless shim exist.
- The non-root lifecycle test passed with fresh named volumes and repeated warm
  hooks; both exported reports contain 156 passing tests. Disposable volumes and
  the task container were removed. Force-retest succeeded for tests/full/compat;
  exported report counters and the four package files were checked on the host.

These checks do not validate WinUI rendering, Windows playback, signing, or
interactive VS/Codespaces setup. No app was launched or package/image published.
