# Headless runtime confidence and performance

## Local aggregate diagnostics

Normal close writes `runtime-counters.json` in the app's storage directory
(unpackaged: `%LOCALAPPDATA%\CascadiaCollections\ShoutkitWindows`; packaged:
the package's `LocalFolder`). It atomically replaces the last session report:
schema version, UTC capture time, and 26 fixed-name integer counters only.
Counts start at zero for each process; they are not a durable lifetime total.
Forced termination may leave the preceding session's report.

Counters cover scheduled retries/restart requests/exhaustion, native playback
failures and detected Playing-without-progress stalls, accepted/duplicate
metadata, empty/oversize/damaged/non-song rejections, retired callbacks, artwork
memory hits/misses/expiry, catalog hits/shared requests/matches/misses, rejected
responses, transport cancellations, and transport failures. Rejection categories
are intentionally coarse, not a transcript of what a station sent. Artwork
memory counts aggregate encoded and dispatcher-local decoded caches; expiry also
counts as a miss, and a download may check memory again after acquiring a slot.
Cancellations include deadlines, caller cancellation, and normal shutdown, not
just failures. Recovery restart counts are requests, not proof of audible success.
Progress stalls use a monotonic 30-second deadline shared by MediaPlayer and DSP
engines; only forward position resets it. Native device QA is still required.

Hot-path recording uses atomic memory increments with no disk IO. JSON serialization
and atomic replacement run in background work during the final close flush.
Snapshots read each counter atomically but are not transactions across counters.
No URLs, station IDs, names, song/artist text, queries, HTTP bodies, or exception
messages are accepted as labels or persisted. Nothing is sent remotely; existing
Radio Browser reporting and artwork privacy settings are unchanged. Concurrent
increment, detached-snapshot, allow-list, JSON-shape, and atomic-replacement tests
cover the report contract. The existing rotated error log remains separate.

Metadata counters describe ICY sidecar/probe monitoring, not native HLS ID3 cues.
Consecutive damaged cues count once per invalidation until a valid cue resumes.
Redirect-hop assertions use the production policy's explicit redirect handling
(its HTTP client has automatic redirects disabled).

`DiagnosticScenario` injects isolated counter sets into real recovery, ICY
monitoring, memory-cache, artwork transport, and catalog lookup components.
Virtual-time retries and scripted responses assert exact counts for retry budget,
acceptance/rejection/duplicates, monotonic expiry, coalesced lookups, negative
cache hits, successful catalog matches, response rejection, and cancellation.
Production defaults still use the shared process counters. These tests run in
parallel without global-count races and do not introduce dynamic metric labels.
The repeatable soak includes this category and `ParserFuzz` alongside metadata
and transport races; the CI workflow calls the same script.

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

`TransportStress` tests stop a loader with four stalled bodies and twenty queued
requests, verify all accepted requests finish before semaphore disposal, and check
that the caller's HTTP client remains usable. Redirect cycles stop after three
hops; every response is disposed. Advertised body-length mismatches are rejected
before artwork or catalog misses can be cached, and a stalled catalog body has a
whole-request deadline. The normal close drain includes shared station artwork
transport. Native bitmap decode already in progress remains an interactive QA
boundary. The soak script repeats `MetadataScenario` and `TransportStress` along
with the existing engine ownership/race tests.

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

## Integrated metadata scenarios

`MetadataOrchestrationTests` connects the real ICY monitor/parser, view model,
privacy settings, station library, and persisted listening history. Scripted
continuous-stream callbacks and deliberately late catalog responses exercise
rapid titles, station changes, opt-out during lookup, reconnect deduplication,
and damaged-cue artwork fallback. A queued dispatcher reproduces asynchronous
UI delivery without opening a window. Assertions cover title, player-facing
artwork, and history reloaded from disk together.

The player and catalog are controlled test doubles: these tests do not prove
native MediaPlayer decoding, audible playback, XAML layout, or actual SMTC
rendering. Their category is `MetadataScenario` for repeated headless runs.

Additional scenarios hold an artwork dispatcher callback across A → B → A and
verify that the earlier occurrence cannot update the UI, player artwork, or the
wrong persisted history entry. A gated history adapter intentionally hides its
accepted record from `FlushAsync`; shutdown must wait for that record to reach
the real history store before flushing. Post-close queued collection refreshes
are ignored. These tests use explicit gates rather than timing sleeps.

`ParserFuzz` uses two fixed seeds and 5,000 ICY-field mutations per seed. It
checks deterministic results, bounded display fields, and rejection of damaged
Unicode/control text without logging generated song strings. Separate regressions
cover input/field size limits, nested-field recursion, and valid emoji. This is
a reproducible mutation corpus, not exhaustive fuzzing or a wall-clock SLA.
The parser now rejects embedded controls and unpaired UTF-16 surrogates as damaged
cues as well as replacement characters, so a damaged cue clears obsolete track
state instead of sending invalid display text to native media controls.

## Local shutdown/metadata hardening verification (2026-10-02)

Both ARM64 and x64 Release builds/tests passed with warnings treated as errors:
330 tests per architecture. Full formatter verification passed for RadioCore,
the WinUI project, and tests. The expanded 20-test soak suite passed 21 ARM64
iterations and 14 x64 iterations in separate one-minute runs (x64 emulated on
this ARM64 host). Reports are under `out/headless-soak/arm64-shutdown-metadata/`
and `out/headless-soak/x64-shutdown-metadata/`, not committed or treated as timing
SLAs. These iterations include the new metadata and transport scenarios.

A self-contained ARM64 preview was published to
`out/shoutkit-arm64-runtime-hardening/`; its license assets matched the committed
46-package/20-notice inventory and all eight inventory fixture checks passed.
No app, audio session, computer-use interaction, or remote CI run was started.
Native shutdown, audible reconnect, actual media controls, visual/accessibility
interaction, and release signing still require the documented release checks.

## Local Acrylic/parity follow-up verification (2026-10-02)

The final source passed 349 Release tests on each of ARM64 and x64, with warnings
treated as errors. Formatter verification passed for RadioCore, WinUI, and tests.
The expanded 31-test soak passed 20 ARM64 and 13 x64 iterations in one-minute
runs; each iteration includes the 10,000 seeded cue mutations, metadata/history
races, and isolated diagnostic wiring tests. x64 runs are emulated on this ARM64
host. Reports are local under `out/headless-soak/{arm64,x64}-acrylic-parity/`.

The self-contained preview is `out/shoutkit-arm64-acrylic-parity/smodr.exe`.
Published notices matched the unchanged inventory; all eight inventory fixture
checks passed. Close an existing instance before launching a different preview:
the app's single-instance routing otherwise targets the already-running build.
No app, audio, or computer-use session was launched, and no remote CI was run.
Settings/markup tests do not establish rendered blur, legibility, inactive-window
fallback, or native media behavior. Those still need an interactive check. The
backdrop is replaced only when its saved choice actually changes, avoiding native
compositor churn from unrelated settings notifications.
