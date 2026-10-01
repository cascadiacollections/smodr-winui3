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

## Next platform-native slices

1. **Audio-output interruption policy.** iOS has explicit interruption, route-loss, and media-services-reset handling. Windows currently relies on `MediaPlayer` and reconnects stalled streams, but has no policy around [default render-device changes](https://learn.microsoft.com/en-us/uwp/api/windows.media.devices.mediadevice.defaultaudiorenderdevicechanged). Add a small adapter with tests for headphone removal, device replacement, and user pause precedence before wiring it to the player. Do not resume automatically on an unrelated output without an explicit policy.
2. **Deep links and quick launch.** iOS has `holmdel://station` routing, Siri/Shortcuts, Spotlight indexing, and a favorite widget. Windows currently redirects a second activation to the existing window but ignores activation payloads. A packaged [protocol activation](https://learn.microsoft.com/en-us/windows/apps/develop/launch/handle-uri-activation) route can be the shared foundation for a jump list and notification actions. Treat URI station data as untrusted: bound length, accept only HTTPS stream/artwork, reject duplicate or unknown parameters, and route on the UI dispatcher. An unpackaged preview needs its own registration story.
3. **Equalizer and spatial audio.** These exist behind capability checks in iOS, but the Windows radio player has no DSP chain. Do not show controls until a Windows audio pipeline can apply presets to the actual stream, preserve system media controls, and pass latency/resource tests. A simple `MediaPlayer` property is not an equivalent to the iOS engine's equalizer.
4. **Optional SHOUTcast provider.** The iOS directory supports a key-gated SHOUTcast fallback; Windows is Radio Browser-only. Keep this optional and avoid embedding an API key in the client.

Windows widgets, cross-device Handoff, watch/TV targets, and CarPlay do not have one-to-one desktop equivalents. Scope each as a separate product feature rather than treating it as a missing method call. The immediate release gates remain signed package identity, live stream/device-change smoke tests, accessibility, and visual checks; see [RELEASE.md](RELEASE.md).

## Runtime and enlistment follow-up

- Radio Browser payloads are now bounded to 2 MiB, 1,000 scanned records, and 100 returned stations. Requests use response-header streaming so a faulty mirror cannot force an unbounded JSON allocation. Play reporting does not download a response body.
- Genre warmup now re-evaluates the Windows connection-cost and Energy Saver policy between requests, so a newly metered connection or low-power state stops remaining speculative traffic.
- The old podcast view model and supporting services remain compiled but dormant. Keep them isolated until the product decision is made to restore a podcast surface or split them into an archival project; deleting them during radio hardening would erase unrelated functionality.
- The repository pins .NET 11 RC1 and Windows App SDK 2.4. Re-run locked restore, ARM64/x64 tests, unsigned MSIX build, and the disposable signed-install CI lane when changing either. Do not silently advance the package graph to a newer RC/GA during a feature patch.
