# Shoutkit Windows release gate

The downloadable CI artifacts are **unsigned, unpackaged previews**, not installers. They are self-contained folders for internal testing. CI also builds an unsigned MSIX as a packaging validation but does not upload it as a release. Do not label or distribute either as a production release.

## Automated checks

The GitHub Actions build uses the exact .NET 11 RC SDK in `global.json`, locked NuGet restores, warning-free x64 and native ARM64 builds and unit tests, formatting checks, self-contained publish artifacts, and unsigned MSIX package validation for both architectures. The ARM64 lane uses GitHub's Windows 11 ARM64 runner with Visual Studio 2026. Launch and live-playback checks still require an interactive Windows host; CI unit tests do not prove those behaviors.

To validate a package locally without a signing certificate, run `dotnet msbuild smodr/smodr.csproj -restore -p:Configuration=Release -p:Platform=ARM64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never -p:UapAppxPackageBuildMode=SideloadOnly`. The generated MSIX is unsigned and cannot be installed on ordinary machines until signed with the chosen publisher identity.

The current local package build warns that `mspdbcmf.exe` is unavailable, so it does not produce a symbols package. Install the matching Windows development tooling and require a symbols package before a public release.

## Before external distribution

1. Choose the distribution channel: Microsoft Store MSIX, signed direct-download MSIX, or a signed installer for an unpackaged app. MSIX is preferred when package identity, reliable installation/update, and Windows integration are needed.
2. Replace the development identity in `smodr/Package.appxmanifest` (`CN=excel` and a GUID package name) with the publisher identity actually owned by the publisher. Replace the inherited podcast icon/splash artwork with approved Shoutkit assets. Confirm versioning and privacy/support URLs. Do not guess a certificate subject or commit signing secrets.
3. Build a packaged configuration and sign it with a certificate trusted by the chosen channel. Store submissions are signed by the Store; non-Store MSIX distribution requires the publisher's signing setup. Test install, upgrade, uninstall, and rollback on clean x64 and ARM64 Windows systems.
4. Decide how existing unpackaged `%LOCALAPPDATA%\CascadiaCollections\ShoutkitWindows\library.json` data will migrate to the packaged app's data location. Test preservation of favorites and recents across an upgrade, including closing the window immediately after a change. Keep the library schema version and do not overwrite a newer schema.
5. Run manual playback checks with at least two live stations and one dead stream; check initial buffering, a network drop/reconnect, pause or station switch during backoff, Play after the retry budget is exhausted, pause/resume/stop, sleep/wake, device changes, offline behavior, second-launch activation, keyboard and screen-reader navigation, high contrast, and 200% scaling.
   Verify the Radio Browser privacy toggle persists across relaunches and that a new UUID station selection makes one `/json/url/{stationuuid}` request only when enabled; rapid duplicate clicks, pause/resume, and automatic reconnect must not report again. Confirm reporting failure does not interrupt audio.
   The repeatable startup/single-instance check is `./scripts/Test-ShoutkitLaunch.ps1 -ExecutablePath ./out/shoutkit-arm64/smodr.exe`; it closes its test window afterward.
6. Review `diagnostics.log` on failure. It intentionally stores error categories rather than station URLs or response bodies. Define a user-consented crash-reporting policy before adding any remote telemetry.
   The directory snapshot and station artwork caches are best-effort local data. Check cold relaunch with and without connectivity, stale-cache expiry, metered-network and Energy Saver behavior, and that clearing local cache never removes favorites or recents.
7. Move from .NET 11 RC to a serviced GA version when available; republish both architectures and repeat the release gate.

References: [Windows packaging choices](https://learn.microsoft.com/windows/apps/package-and-deploy/packaging/), [MSIX signing](https://learn.microsoft.com/windows/msix/package/sign-msix-package-guide), [WinUI testing](https://learn.microsoft.com/windows/apps/develop/testing/), and [GitHub-hosted Windows ARM64 runners](https://docs.github.com/actions/reference/runners/github-hosted-runners).
