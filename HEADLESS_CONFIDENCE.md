# Headless runtime confidence and performance

## Shutdown lifecycle

`BackgroundWorkScope` registers work before invoking it, seals new admission during
shutdown, cancels the shared lifetime, and drains all accepted operations. Metadata
monitoring, album lookup, prewarm preparation/expiry, DSP creation, and view-model
search/selection/history/report tasks use this boundary. The async operation owns its
cancellation source until cleanup finishes; an obsolete artwork callback is also
checked against a generation, even if the same song appears again.

Normal window close initiates native teardown on the player dispatcher, waits up to
15 seconds for background shutdown, then independently allows up to 15 seconds to flush
durable saves. A timeout/error is recorded locally and close proceeds. Accepted history
and library writes are drained rather than discarded. Forced termination cannot promise
these guarantees; an uncooperative provider can outlive the shutdown deadline. The
existing atomic-write and schema-preservation rules remain unchanged.

Album requests have an eight-second whole-request deadline including body reads. One
listener's cancellation still does not abort another listener's shared request; app
shutdown cancels the shared transport. Injected fakes test body cancellation, retired
metadata probes, prewarm cleanup, and concurrent admission versus shutdown without audio.

These checks do not open WinUI, play audio, contact stations, or use your saved profile.
They use disposable synthetic profile files, fake engines, virtual time, and a loopback
HTTP listener. The performance harness also invokes the Windows bitmap decoder directly,
without a window or audio device. There are no new third-party dependencies.

## Race coverage

`RadioRuntimeRaceTests` checks rapid MediaPlayer/AudioGraph identity replacement,
late startup/failure/completion delivery, retry invalidation after pause or station
switch, shutdown while events are queued, prepared-resource ownership transfer,
source-less DSP bridge retention, and concurrent Take/Clear/Dispose. Playback controls
reject calls after coordinator disposal instead of silently succeeding.

These are tests of the production coordinator, recovery controller, and generic warmup
slot. Fake engines cannot prove native MediaPlayer/AudioGraph startup, prepared-source
decoder handoff, audibility, or the complete AudioService/WinUI interaction.
Teardown-failure coverage also checks that an incoming engine is retired if the previous
engine's disposal throws, and that failed shutdown still leaves the coordinator disposed.

## Soak tests

Each `Soak` pass performs 500 virtual-time reconnect cycles, 5,000 concurrent artwork
cache updates, 256 persisted history records plus late artwork updates and reload,
and 128 real socket metadata reads. Assertions cover single disposal per retired
engine, no post-shutdown retry, cache entry/weight/expiry bounds, bounded history without
resurrecting evicted entries, and continued progress through one HTTP connection slot.
They are structural resource checks, not native-memory leak measurements.

After the usual locked solution restore, run from the repository:

```powershell
./scripts/Test-HeadlessSoak.ps1 -Platform ARM64 -Minutes 5
./scripts/Measure-HeadlessPerformance.ps1 -Platform ARM64 -Runs 5
```

Use `-Platform x64` with an x64 SDK to compare the x64 path. `-DotnetPath <path>` selects
the locally installed pinned SDK executable. The soak runner stops on the first build
or test failure and retains per-iteration TRX files. Default output folders under `out/`
have unique IDs; explicit `-OutputDirectory` is useful in CI. The duration is a minimum:
an in-progress bounded test pass completes before stopping.
`-SkipBuild` reuses an already-built Release configuration for that platform; CI uses
it only after its warning-as-error solution build. Do not use it against stale outputs.

## Performance scope and interpretation

Each fresh performance process creates its own fixtures, warms each case three times,
then records 25 samples. JSON reports include runtime, OS, architecture, fixture sizes,
minimum/median/p95 milliseconds, and process-wide managed allocations per operation.

| Measurement | Includes | Does not establish |
| --- | --- | --- |
| Startup preparation | Read synthetic library/history and persisted 60-station directory snapshot | Window activation, first frame, cold OS disk cache |
| Warm list copy | Read/copy cached 60-station snapshot | ListView layout or scroll responsiveness |
| Native artwork decode | Stream setup and Windows BMP decode from 512x512 to 128x128 BGRA | BitmapImage/GPU rendering, codec diversity, network fetch |
| Encoded cache hit | Retrieve the same cached byte array | Native decoded-image memory retention |
| License read/split | Read actual bundled notices and make bounded text sections | Modal opening or XAML text layout |
| License split only | Lossless bounded text-section preparation | UI-thread time or warm dialog reuse |

The wrapper's process duration includes fixture setup and **all** cases. It is not an
application startup measurement. OS file caches are not flushed; managed allocation
counts include other harness threads and exclude native/GPU allocations. p95 from
25 samples is diagnostic, not a statistically rigorous SLA. Host load, JIT, power
policy, architecture/emulation, and filesystem state influence results.

No timing or GC-memory thresholds fail CI. Correctness, report schema, architecture,
and finite measurements are gates; timings are artifacts for comparing like-for-like
runs. Establish multiple stable baselines before adding performance regression budgets.
GitHub Actions runs a one-minute soak and three performance processes per Windows
architecture and retains the reports; local workflow edits are not proof of a remote run.

Interactive playback, SMTC, DSP/prewarm switching, native memory profiling, accessibility,
and measured license-dialog responsiveness remain in [RELEASE.md](RELEASE.md).

## Local baseline: 2026-10-02

Clean Release builds passed with warnings treated as errors, and all 311 tests passed
on both architectures. Final one-minute soak runs passed 28 iterations on ARM64 and
15 on x64, each with all 11 race/soak cases passing. Three fresh performance processes ran per architecture on
Windows build 28120, with .NET 11 RC1 runtime `11.0.0-rc.1.26425.128`. ARM64 is native;
x64 runs under emulation on this ARM64 host. The fixture contains 20 favorites/history
entries, 60 directory stations, 1,140,842 notice characters, and a 786,486-byte BMP.

Ranges below are **medians across the three processes**, in milliseconds, not improvement
claims or release budgets. Tiny cache-hit timings are particularly sensitive to harness
overhead and timer granularity.

| Case | Native ARM64 median range | Emulated x64 median range |
| --- | --- | --- |
| Profile/directory preparation | 0.685–0.708 | 0.937–1.016 |
| Warm 60-station copy | 0.0004 | 0.0007 |
| Native BMP decode/downscale | 0.435–0.444 | 0.642–0.721 |
| Encoded artwork cache hit | 0.0002 | 0.0003 |
| License read and split | 3.444–3.947 | 4.222–4.620 |
| License split only | 0.183–0.284 | 0.281–0.288 |

Raw local JSON is under `out/headless-performance/arm64-final/` and
`out/headless-performance/x64-local/`; timings, allocation counts, and p95 outliers are
retained there, not committed as machine-independent expectations. The local workflow
has not been pushed or run remotely.
