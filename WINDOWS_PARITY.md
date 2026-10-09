# Windows platform and iOS parity audit

This is a source-level audit of this checkout against the sibling `shoutkit` iOS checkout. It is not a claim that every path has been exercised on a live stream or an installed MSIX. Prefer Windows-native behavior over a literal SwiftUI port.

See [Radio Browser API usage audit](RADIO_BROWSER_API_AUDIT.md) for upstream iOS guidance gaps, source anchors, and the Windows adaptations.

## Already represented

### Upstream sync (2026-10-07)

Re-audited against iOS `origin/main` at `d10868f`. Since `a57dd0b` upstream
landed only #220 (dependency refresh, album-art request coalescing, http(s)+host
validation of upsized artwork URLs) and a CodeQL action bump. Windows already
coalesces lookups under one lock (`AlbumArtworkLookup`) and is stricter on URLs
(HTTPS, `mzstatic.com` / Apple store hosts only); nothing to port. Upstream
branches `codex/playback-audit-fixes` and
`copilot/extract-libraryfeaturecore-and-settingsfeaturecore` were already
squash-merged (#208, #203); `claude/rust-swift-ffi-spike-*` is an unmerged uniffi
spike with no Windows impact yet.

Closed in this pass (headless tests plus a sandboxed `LOCALAPPDATA` run on
Windows 11 ARM64 driven through UI Automation):

- **UI Automation crash (pre-existing, release-blocking).** `async Task Main`
  dropped `[STAThread]` from the synthesized entry point, so XAML ran MTA and the
  first out-of-process UIA query (Narrator, Voice Access, Inspect) crashed the
  process with 0xc0000005. `Main` is synchronous again and a metadata test pins
  the attribute. List items also announced `smodr.Models.RadioStation`; list
  models now have readable `ToString()` values.
- **Library editing** (`LibraryListEditing`, `SavedStationsNotice`): drag,
  Alt+Up/Down and context-menu reordering of favorites; 10-second Undo after
  removing a favorite; confirmed Clear for Recently Played and listening
  history (which also resets Top Tracks).
- **Recovery UX** (#208): "Reconnecting…" during automatic rejoins and a Retry
  action once the retry budget is spent.

Still missing versus iOS/Android, highest value first: favorites export/import,
search filters (bitrate/tag/country; the client already supports bitrate),
localization (`.resw`; iOS ships 10 catalogs), first-run welcome, initials
placeholder artwork, directory-unavailable empty state with Retry, Share
(`DataTransferManager`), fade-in on rejoin, and the iOS `SongTitleFilter`
station-name heuristics. Android-only (`sir-android`): home widget, Quick
Settings tile, pinned play shortcuts, headless play links, resume on headphone
reconnect. `cascadia-audio-win-mvp` (Rust/cpal, ffmpeg CLI for AAC) is superseded
by the engines in [AUDIO_ENGINES.md](AUDIO_ENGINES.md).

### Focused runtime/settings audit (2026-10-02)

Compared the local iOS checkout at `a57dd0b`; no upstream files were changed or
remote updates assumed. Source anchors: iOS `SettingsStore.swift`,
`FeatureFlags/Feature.swift`, `AppDependencies+Callbacks.swift`,
`LibraryStore+RecentlyHeard.swift`, and `PlaybackController+Interruptions.swift`.
Windows anchors: `RadioPrivacySettings`, `RadioPlaybackPreferences`,
`RadioSettingsViewModel`, `RadioMainViewModel`, `TrackHistoryService`,
`AudioService`, and `LiveRadioRecovery`.

| Area | Confirmed behavior and remaining difference |
| --- | --- |
| Play reporting | Both fresh profiles default on and expose an off switch. Windows reports explicit trusted selection/resume, not background recovery or untrusted link UUIDs. UUID validation and mutation retry policy are covered by existing tests. The UUID-only payload does not hide the requesting IP; changing the switch cannot undo an already-sent request. |
| Artwork consent | Both default on; Windows opt-out cancels current lookup, restores station artwork, and rejects late catalog results. Lookup-disabled history stays local and has no newly attached catalog artwork. Previously saved covers are not retroactively erased by this switch. |
| Listening history | Both retain up to 1,000 local entries and deduplicate consecutive station/title/artist repeats, preserving monotonic duplicate timestamps. Neither inspected settings store exposes a history-off toggle. Windows captures audio cue callback arrival time before UI dispatch/persistence and provides local JSON export plus confirmed clear. This is callback receipt time, not a broadcaster timestamp. |
| Playback settings | Looping and speculative stream prewarming default off in both. Windows has Off/Speech/Bass/Treble DSP presets; this is not identical to the iOS equalizer catalog. Windows jump lists and Acrylic are platform-specific additions. |
| Feature flags | iOS has persisted default/enabled/disabled overrides for diagnostics, geo stations, prewarming, and Live Activity. Windows exposes concrete playback toggles, not that general override catalog. Do not describe settings as complete feature-flag parity. |
| Optional OS features | iOS diagnostics collection and precise geo permission are independently off by default; spatial rendering is opt-in. Windows has a bounded local exception log and user-requested export of aggregate counters, without remote collection. Precise geolocation, spatial rendering, and a Live Activity equivalent still require deliberate Windows UX/API design. |

Runtime fixes in this pass:

- A momentary `Playing` signal no longer resets retry attempts. Thirty seconds of
  continuous healthy playback earns a fresh budget; repeated Playing events do
  not restart that healthy-window clock. Retry delays remain exponential and
  bounded, and invalid/overflowing timer configuration fails synchronously.
- Opening as well as Buffering arms recovery on both media-engine paths. Repeated
  buffering notifications cannot move the existing deadline forward.
- Metadata delivery is rechecked against source/playback intent; queued obsolete
  titles, clears and station notifications cannot overwrite current player state.
  Accepted latest titles keep artwork and durable history aligned. UI delivery
  intentionally discards superseded notifications; this is not an exhaustive
  log of every raw ICY callback received while the UI was busy.
- Artwork cancellation ownership now uses atomic exchange/compare-exchange so
  an older lookup's background completion cannot clear a newer cancellation
  owner. Existing late-result and opt-out regressions continue to exercise it.
- Virtual-time recovery tests cover brief flapping, stable recovery, resume/pause
  epoch invalidation, and fixed buffering deadlines. Queue-controlled metadata
  tests include reverse delivery of obsolete title/clear callbacks.

Existing engine-generation, shutdown, route-change and artwork cancellation tests
remain in place. Windows now subscribes to Windows App SDK
`PowerManager.SystemSuspendStatusChanged` and WinRT
`NetworkInformation.NetworkStatusChanged`, dispatching observations to the player
owner thread. Sleep or loss of every connected profile retires sockets and
metadata, clears speculative preparation and cancels recovery timers. Selected
station/user intent is retained independently; Pause/Stop cancels that intent,
and a station chosen offline replaces it without opening a stream.

Playback Settings now durably expose **Resume after Windows sleep** (default off)
and **Resume after network loss** (default on). Overlapping interruptions require
both conditions restored and both permissions. Reconnection does not generate a
new user play report. Local/constrained connections remain eligible: NCSI's
InternetAccess verdict is not a requirement for LAN radio. Connected-to-connected
VPN/interface changes do not forcibly restart working audio; native failure and
buffering recovery still handle those cases. OS reads that fail preserve the last
known condition; subscription failures are local diagnostics, not startup failures.

Pure-policy tests exercise duplicate events, overlapping interruptions, settings,
manual pause/stop, already-paused streams and offline selection. Durable-settings
tests prove old schema-1 files inherit the new defaults without losing choices.
Headless XAML tests verify the added controls' names, busy gating and bindings.
This closes the explicit coordination code gap, not real-device validation.

Both playback engines now use a monotonic 30-second progress deadline while
reporting Playing. Forward position extends the deadline; identical or backward
native samples do not. Buffering, pause, source retirement and a new source stop
or reset the appropriate timer. A frozen MediaPlayer therefore enters the same
bounded recovery budget as explicit MediaFailed and buffering failures. Aggregate
local counters distinguish native failures from detected progress stalls without
recording station or track data. Windows exposes no direct AVAudioSession-style
`mediaServicesWereReset` callback for this MediaPlayer path: `MediaFailed`, output
device changes, power/connectivity coordination and progress detection are the
recovery boundaries. Real-device reset behavior remains native QA.

Headless validation does not prove audible recovery after real sleep, Bluetooth
handoff, VPN changes, or prolonged network loss. The native QA gates below remain
open. Existing modified Windows test lockfile was left untouched during this audit;
isolated restore metadata was used for local Windows test execution.
Final headless results for this pass: 356 tests on Windows ARM64, 356 on Windows
x64, and 161 in the fresh Linux ARM64 Docker full build. Four package builds,
the offline package consumer, fully trimmed consumer, and portable formatting
verification also passed. Windows build/test outputs are isolated under
`out/runtime-audit` and `out/runtime-audit-x64`; Docker reports remain under
`out/docker-validation`. No app UI, physical playback, or live station was launched.

| iOS behavior | Windows implementation |
| --- | --- |
| Listen Now, search, favorites, recents, shared player | WinUI `NavigationView`, `RadioMainViewModel`, local library |
| Lock-screen/Control Center transport and now-playing text | `MediaPlayer` display properties and Play/Pause command manager |
| Live ICY and HLS timed-ID3 titles | Continuous ICY sidecar with bounded probe fallback; native timed cue listener |
| Local listening history, top tracks, sleep timer | Bounded JSON history, aggregation, one-shot timer |
| Album-art lookup and Apple Music link | Plausibility-checked iTunes lookup with station-art fallback |
| Radio Browser play reporting | UUID-only request with a device-local privacy switch |
| Bounded reconnect and warm browse | `LiveRadioRecovery`, snapshot cache, Windows network-cost/Energy Saver gate rechecked before each speculative genre request |
| Station deep links | Packaged `holmdel://station` / `holmdel://play` activation, strict HTTPS payload parsing, single-instance routing |
| Default output changes | Native `MediaDevice` event pauses requested radio playback; no surprise auto-resume |
| Play-reporting and album-artwork choices | Persisted privacy switches, matching the two iOS defaults |
| In-app software licenses | Offline-readable app license and runtime dependency inventory in Settings |
| Reorder/remove saved stations with undo | ListView drag, Alt+Up/Down and Move up/down menu; InfoBar Undo restoring the former position |
| Clear listening history | Confirmed Clear for Recently Played and Recently Heard; Top Tracks recomputed |
| Reconnecting state and Retry | `LiveRadioRecovery.IsReconnecting` mapped to "Reconnecting…"; InfoBar Retry after the budget is spent |

## Next platform-native slices

1. **Audio-route validation.** iOS handles interruption, route loss, and media-services reset. Windows now observes [default render-device changes](https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.mediadevice.defaultaudiorenderdevicechanged) and pauses when the selected output changes during requested playback. Test headset removal, docking, Bluetooth handoff, explicit user pause, and resumed playback on real hardware; Windows may switch output before the event is delivered. A default-output change is not proof that a device was physically removed.
2. **Quick launch validation.** Packaged favorites/recents now publish bounded ID-only Windows jump-list items and handle cold/redirected activation against the saved library. Already-playing stations are not toggled off; unknown IDs do nothing. Protocol links retain their existing strict payload/telemetry rules. Native MSIX activation and removal persistence need QA; unpackaged jump lists are unsupported. App-search indexing/widgets remain separate work.
3. **Equalizer validation and spatial audio.** Experimental Speech/Bass/Treble presets now process the actual stream through Windows AudioGraph, preserve manual SMTC and ICY metadata, and use opening/progress deadlines plus existing recovery. Off retains MediaPlayer. HLS timed-ID3 and prewarming are unavailable on the DSP path. Native audibility, decoder compatibility, route changes, latency, CPU/memory, and cleanup remain release gates. Spatial audio is not implemented.
4. **Optional SHOUTcast validation.** A process-environment developer key enables a bounded HTTPS legacy directory fallback with keyless PLS tune-in resolution. The key is not persisted or embedded. Resolution is cancellable/version-guarded and non-UUID station IDs do not submit Radio Browser telemetry. Synthetic coverage exists; developer access/terms review and live key-backed smoke tests are still required.
5. **Settings and flags.** Windows now exposes opt-in finite-broadcast looping, genuine native saved-stream preparation, and experimental equalizer presets through durable Playback settings. Invalid/future settings fail closed without overwriting their files. Diagnostics remain local category/type/HResult records; there is no instrumentation/export toggle, precise-location feature, spatial audio, or Live Activity equivalent. The iOS internal prewarming/location/instrumentation flags are not all ordinary Release Settings. Windows genre snapshot warmup and native stream preparation are separate features; neither guarantees a startup latency improvement.

6. **ARM64 startup: ReadyToRun.** Measured 2026-10-07 on Windows 11 ARM64 (self-contained `win-arm64` publish, sandboxed profile, 5 warm launches each, time from process start to the titled main window): JIT median 601 ms (568–2820), `PublishReadyToRun` median 478 ms (408–630), about 20% faster for +57 MB (308 vs 251 MB). Not yet enabled: `publish --no-restore` fails with NETSDK1094 because the Crossgen2 pack is only restored for a RID-specific restore, which adds RID sections to every lockfile. Enable it together with a RID-aware locked restore (`-r win-arm64` / `win-x64`) and the CI/MSIX publish steps in one change. Trimming and Native AOT come after .NET 11 GA.
7. **Toolchain.** Windows App SDK 2.5.1 is the latest stable (2.4.0 pinned; confirmed via the NuGet v2 feed because `api.nuget.org` is blocked on the reference host). .NET 11 is still RC1. Bump each separately with the full locked restore, ARM64/x64 tests and MSIX lanes, and raise the manifest's `MaxVersionTested` at the same time.

Windows widgets, cross-device Handoff, watch/TV targets, and CarPlay do not have one-to-one desktop equivalents. Scope each as a separate product feature rather than treating it as a missing method call. The immediate release gates remain signed package identity, live stream/device-change smoke tests, accessibility, and visual checks; see [RELEASE.md](RELEASE.md).

## Runtime and enlistment follow-up

- Radio Browser payloads are bounded to 2 MiB, 1,000 scanned records, and 100 returned stations. Requests use response-header streaming so a faulty mirror cannot force an unbounded JSON allocation. Play reports validate the API's `ok` result with a 16 KiB limit; response bodies have an eight-second deadline.
- Directory mirrors are discovered using the official aggregate DNS hostname and reverse lookup, cached for six hours, and shuffled for each operation. DNS failures retain stale mirrors or use the official aggregate hostname, with a one-minute discovery retry delay. Requests identify the app and assembly version. Directory responses use `countrycode`; legacy saved country names remain readable. Explicit user starts and resumes report only trusted directory UUIDs when enabled; pause and automatic recovery do not report.
- Genre warmup now re-evaluates the Windows connection-cost and Energy Saver policy between requests, so a newly metered connection or low-power state stops remaining speculative traffic.
- Directory reads now have one bounded per-host retry through `Microsoft.Extensions.Http.Resilience`. Radio Browser play reports use a separate client without that handler, so a lost response is not automatically replayed. Live ICY and artwork traffic retain their existing policies. Sleep-timer tests use virtual time; Settings uses Windows Community Toolkit cards.
- Product scope is radio-only. The dormant podcast models, services, view model, tests, converters, and RSS dependency were removed by explicit product decision. Git preserves their history; radio profile files are untouched.
- Privacy settings use a typed, schema-versioned snapshot. `RadioSettingsViewModel` now owns edit gating, save errors, durable-choice refresh, and offline license loading; `MainWindow` only forwards toggle actions and displays the native license dialog. Privacy and Playback controls bind to their busy/read-only state and durable values, and a Settings-local error bar cannot be overwritten by playback status. Failed saves restore the last durable choice before edits are re-enabled. Headless coverage checks initialization, duplicate/reentrant edits, rollback/retry, playback-aware artwork updates, license-read failures, and XAML binding wiring; existing persistence tests retain network-privacy defaults, concurrent updates, legacy migration, and corrupted/future-file fail-closed behavior.
- The in-app license view now ships generated notices for the complete locked app dependency graph and restored self-contained .NET runtime packs, including bundled Windows App SDK license/third-party text. CI rejects stale text/JSON and missing or changed published inventory assets. See [LICENSE_INVENTORY.md](LICENSE_INVENTORY.md) for regeneration and scope. Before external redistribution, still audit the exact signed binary graph, vendor redistribution conditions, packages without bundled terms, and any framework/native components outside the inventory; automation is not legal approval.
- The Windows repository declares MIT while the upstream Shoutkit iOS application declares GPL-3.0. Review provenance of transplanted/translated code and resolve license compatibility and notices before distribution; the current repository label alone does not establish that every Windows source file can be redistributed under MIT.
- The repository pins .NET 11 RC1 and Windows App SDK 2.4. Re-run locked restore, ARM64/x64 tests, unsigned MSIX build, and the disposable signed-install CI lane when changing either. Do not silently advance the package graph to a newer RC/GA during a feature patch.

Implementation details and native validation steps for these completed code slices are in [PLAYBACK_FEATURES.md](PLAYBACK_FEATURES.md). Their native QA gates remain open; synthetic tests are not evidence of audible output or measured prewarming gains.
