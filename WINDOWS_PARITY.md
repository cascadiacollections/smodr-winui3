# Windows platform and iOS parity audit

This is a source-level audit of this checkout against the sibling `shoutkit` iOS checkout. It is not a claim that every path has been exercised on a live stream or an installed MSIX. Prefer Windows-native behavior over a literal SwiftUI port.

## Already represented

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

## Next platform-native slices

1. **Audio-route validation.** iOS handles interruption, route loss, and media-services reset. Windows now observes [default render-device changes](https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.mediadevice.defaultaudiorenderdevicechanged) and pauses when the selected output changes during requested playback. Test headset removal, docking, Bluetooth handoff, explicit user pause, and resumed playback on real hardware; Windows may switch output before the event is delivered. A default-output change is not proof that a device was physically removed.
2. **Quick launch beyond protocol links.** Packaged [protocol activation](https://learn.microsoft.com/en-us/windows/apps/develop/launch/handle-uri-activation) now handles bounded iOS-style station links. `autoPlay=0` opens Search for the station name; `presentNowPlaying` is accepted for link compatibility but Windows has no matching presentation mode. A Windows jump list or notification action can emit these links next. The unpackaged preview does not register a protocol, and a native app-search index/widget remains separate work.
3. **Equalizer and spatial audio.** These exist behind capability checks in iOS, but the Windows radio player has no DSP chain. Do not show controls until a Windows audio pipeline can apply presets to the actual stream, preserve system media controls, and pass latency/resource tests. A simple `MediaPlayer` property is not an equivalent to the iOS engine's equalizer.
4. **Optional SHOUTcast provider.** The iOS directory supports a key-gated SHOUTcast fallback; Windows is Radio Browser-only. Keep this optional and avoid embedding an API key in the client.
5. **Settings and flags.** The iOS Release screen also offers loop-finished-broadcasts and local-only diagnostics collection. Windows has no looping choice; implement it only after defining media-ended vs. reconnect semantics and testing finite streams. Windows already records local diagnostic categories without MetricKit, and has no export/share path, so an iOS-shaped diagnostics toggle would currently misrepresent its effect. iOS equalizer and spatial controls are capability-gated; see item 3. Its geo-stations, prewarming, diagnostics instrumentation, and Live Activity flags are internal and exposed for overrides only in Debug/TestFlight, not ordinary Release Settings. Windows' unrestricted-connection genre warmup is a different feature and should not be labeled equivalent to station DNS/TLS prewarming. Precise-location controls require a real Windows location feature and permission flow first.

Windows widgets, cross-device Handoff, watch/TV targets, and CarPlay do not have one-to-one desktop equivalents. Scope each as a separate product feature rather than treating it as a missing method call. The immediate release gates remain signed package identity, live stream/device-change smoke tests, accessibility, and visual checks; see [RELEASE.md](RELEASE.md).

## Runtime and enlistment follow-up

- Radio Browser payloads are now bounded to 2 MiB, 1,000 scanned records, and 100 returned stations. Requests use response-header streaming so a faulty mirror cannot force an unbounded JSON allocation. Play reporting does not download a response body.
- Genre warmup now re-evaluates the Windows connection-cost and Energy Saver policy between requests, so a newly metered connection or low-power state stops remaining speculative traffic.
- Directory reads now have one bounded per-host retry through `Microsoft.Extensions.Http.Resilience`. Radio Browser play reports use a separate client without that handler, so a lost response is not automatically replayed. Live ICY and artwork traffic retain their existing policies. Sleep-timer tests use virtual time; Settings uses Windows Community Toolkit cards.
- The old podcast view model and supporting services remain compiled but dormant. Keep them isolated until the product decision is made to restore a podcast surface or split them into an archival project; deleting them during radio hardening would erase unrelated functionality.
- Privacy settings now use a typed, schema-versioned snapshot. Failed saves restore the last durable choice, and Settings disables both switches during a write before showing any failure. The remaining refactor is to move `MainWindow` event-handler orchestration into a dedicated settings presentation model, while retaining tests for network-privacy defaults, concurrent updates, legacy migration, and corrupted/future-file fail-closed behavior.
- The in-app license view ships the app's MIT text and direct runtime package notices. Before external redistribution, audit the exact packaged binary graph, include any additional vendor-mandated notice text (including Microsoft Windows App SDK license/third-party notices), and keep the displayed inventory synchronized with locked package upgrades.
- The Windows repository declares MIT while the upstream Shoutkit iOS application declares GPL-3.0. Review provenance of transplanted/translated code and resolve license compatibility and notices before distribution; the current repository label alone does not establish that every Windows source file can be redistributed under MIT.
- The repository pins .NET 11 RC1 and Windows App SDK 2.4. Re-run locked restore, ARM64/x64 tests, unsigned MSIX build, and the disposable signed-install CI lane when changing either. Do not silently advance the package graph to a newer RC/GA during a feature patch.
