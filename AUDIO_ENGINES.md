# Native audio engine ownership

`AudioService` owns station selection, user intent, recovery, finite-stream looping,
ICY/native timed metadata, and Windows system media controls. Native decoder ownership
and ordinary playback controls are separated behind `IRadioAudioEngine`:

| Engine | Owns | Used for |
| --- | --- | --- |
| `MediaPlayerRadioEngine` | MediaPlayer and its MediaSource, including transferred prewarmed sources | Default playback |
| `AudioGraphRadioEngine` | AudioGraph, input nodes, and MediaSource | Opt-in equalizer playback |

`RadioAudioEngineCoordinator` routes Play, Pause, volume, duration, state, completion,
and failure through one active engine. Owner mutations happen on the player dispatcher.
Native callbacks are checked against both engine identity and a replacement generation
before dispatch and again on delivery. A queued callback cannot affect a replacement
station, even if the same engine instance is later rebound.

The coordinator detaches and disposes a replaced engine by default. DSP explicitly
retains a **source-less** MediaPlayer as its Windows SMTC host; it does not open a second
stream or decoder. AudioService releases that bridge during player teardown. An
assertion rejects a bridge with a source. Concrete engine handles remain only where
Windows-specific capabilities are needed: timed tracks/SMTC and the DSP progress
watchdog. Native ownership is not moved into the view model.

Both paths use the same now-playing metadata builder, completion policy, retry budget,
and volume coordinator. Native error text is not exposed through the engine events;
diagnostics record categories rather than potentially sensitive stream URLs. Experimental
DSP remains opt-in; this refactor does not widen its codec support or change defaults.

## Verification

Coordinator tests use fake engines, without opening an audio device, to check both
paths' controls, volume bounds, ownership transfer, disposal, failed binding, and stale
callback rejection. Metadata tests cover station fallback and track/artist attribution.
The [loopback harness](STREAM_HARNESS.md) checks real HTTP transport independently.

Still required before release: native playback, DSP and prewarm switching, output-device
changes, finite-stream looping, HLS metadata, SMTC artwork/buttons, and shutdown during
buffering. These headless tests do not establish audible quality, native buffering
behavior, UI responsiveness, or release-signing readiness.
