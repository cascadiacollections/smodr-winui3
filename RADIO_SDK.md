# Reusable radio SDK extraction

## Boundaries

| Library | Responsibilities | Dependencies |
| --- | --- | --- |
| Cascadia.RadioBrowser | Wire models, typed filters/pages/rankings, UUID/URL lookup, country-code/language/tag/codec facets, explicit clicks/votes, DNS mirrors, bounded JSON transport | BCL only |
| Cascadia.Radio | Existing domain models, lifecycle tracking, atomic IO, aggregate counters, host-configured diagnostics | BCL only |
| Cascadia.Radio.Metadata | ICY decoding/parsing, continuous reader and monitor, HLS ID3 parsing | Cascadia.Radio |
| Cascadia.Radio.Services | Optional album lookup, artwork caches/loaders, favorites/recents, listening history, directory snapshots | Cascadia.Radio and Metadata |
| smodr.RadioCore | App compatibility adapters, privacy/preferences, SHOUTcast and playback policy | Reusable libraries |

The four libraries target stable .NET 10, not preview runtimes. Windows app/engine/UI remain .NET 11 RC. Only RadioBrowser advertises trimming/AOT compatibility today. Persistence/catalog modules still use reflection JSON.

Reusable persistence requires a host-selected path, resolved once; there are no implicit writes to the Shoutkit profile. App composition supplies its existing paths, preserving profile layout and file schemas.

The app is the first real consumer, not a separate duplicate implementation. Its existing interfaces map SDK wire stations into presentation models. Existing smodr.Models/Services namespaces are deliberately retained in extracted domain/metadata/service modules to keep this migration safe; **those APIs are pre-release and not frozen**. The clean RadioBrowser namespace is independent of app models.

## Transport and API policy

Endpoint/query contracts follow the [official Radio Browser API documentation](https://de1.api.radio-browser.info/). UUID lookup uses the documented uuids query; country-code fields avoid deprecated country names.

Consumer identity is required in ClientOptions.UserAgent, attached per request without modifying shared HTTP defaults. Supply separate non-retrying HTTP clients for writes. The library owns neither HTTP clients nor mirror providers. Configure TimeProvider/discovery delegates for deterministic tests and optional IClientDiagnostics for fixed-category events, with no listener data or disk IO.

Read responses have a 2 MiB ceiling, 1,000 scanned rows, at most 100 returned stations and an eight-second default **per-mirror** deadline covering headers and body. Strict advertised-length validation rejects truncation; invalid/malformed/oversize responses fail over. Caller cancellation stops failover. Pages reject invalid limits/offsets. The SDK does not sanitize or download station artwork/homepage URLs; hosts own that security policy.

DNS discovery coalesces refreshes, filters official HTTPS mirror roots, caches for six hours, randomizes each returned snapshot and retains known mirrors during outages. Failed refresh backs off one minute; the official aggregate hostname is the first-launch fallback. One canceled waiter does not cancel discovery for others.

Search/rank/tag/UUID/URL reads never report a click. RegisterClickAsync and VoteAsync require explicit caller action and consent. They use one mirror, with no SDK retries or failover. Injected HTTP retry/redirect handlers remain the host's responsibility. **App compatibility adapter retains its previous best-effort mirror failover for reports; ambiguous failures can duplicate reports.** Neither layer promises exactly-once delivery.

Not every administrative Radio Browser endpoint is implemented: station edits, checks, change feeds and server administration remain out of scope. This is an independent MIT implementation, not code copied from RadioBrowser.NET and not a drop-in replacement.

## Verification and versioning

Cascadia.Radio.Tests targets .NET 10 without the app, WinUI or Windows SDK. Selected existing tests are linked, not copied, so Windows integration and portable runs share the same assertions. They cover parser fuzzing, metadata lifecycle, bounded transport/artwork, concurrent persistence, and direct SDK contracts. The root Windows solution retains its integration suite.

SDK packages are local pre-release 0.1.0 artifacts. Package validation is enabled. Before public release, freeze clean domain/service namespaces, add full XML documentation, choose package identities, establish a released compatibility baseline and independently review the public API. Nothing here publishes packages or pushes Git changes.

## Repeatable checks

Install stable .NET 10 alongside the Windows app's pinned .NET 11 RC. Portable tests and samples have scoped stable global.json files.

```powershell
Set-Location Cascadia.Radio.Tests
dotnet restore --locked-mode
dotnet test -c Release --no-restore -warnaserror
dotnet format --no-restore --verify-no-changes
Set-Location ..
./scripts/Test-RadioPackages.ps1
./scripts/Test-RadioApiCompatibility.ps1
docker build -f scripts/radio-sdk.Dockerfile -t shoutkit-radio-sdk-local .
```

The package script packs all four modules with SDK validation, then runs an offline consumer against packages, not project references. A fresh isolated cache prevents stale same-version drafts. Samples intentionally omit package hash lockfiles for locally rebuilt drafts; library/test third-party restores remain locked.

Pass -BaselineDirectory pointing to **immutable** 0.1.0 packages to compare future builds against a chosen baseline; never use the output directory being rebuilt. The negative API fixture creates a broken package and requires CP0001 for removed public types. This proves the compatibility gate is active, but does not substitute for a released baseline.

The directory-only trim consumer exercises source-generated station/facet JSON and explicit reporting offline. IsAotCompatible enables AOT analyzers; native AOT execution is not yet verified. Persistence/catalog modules are excluded from trimming claims.

Docker uses isolated artifacts, runs tests, packs all modules, runs the offline package consumer, and publishes/executes the trimmed consumer. Its default RID is this host's Linux ARM64 environment. It downloads build dependencies but never opens UI/audio, contacts public streams, or sends Radio Browser mutations.

The portable CI workflow runs stable .NET 10 on Windows and Linux x64, retains packages/test evidence, and executes trimmed code on Linux. It has not been dispatched remotely. VS Code/Zed include portable test tasks; the dev container installs both SDK tracks. The root Windows .slnx and legacy .sln include extracted libraries; portable tests stay separate from Windows integration.

## Local evidence (2026-10-02)

- Windows app: 349 tests passed on ARM64 and x64 after extraction.
- Portable stable .NET 10: 156 tests passed on Windows ARM64.
- Linux ARM64 Docker: initial 152-test suite passed, four packages built, offline consumer passed, fully trimmed consumer published/executed successfully.
- Docker Desktop was manually paused after that successful build. The final four portable cases and explicit-storage refinements were verified on Windows; their final Linux rerun remains for CI or an unpaused host.
- API compatibility rejected a deliberately broken package; app licenses remained 46 packages/20 notices with eight inventory fixture checks.

This is headless evidence, not visual QA, native audio/AOT execution, signing, or a public package release.
