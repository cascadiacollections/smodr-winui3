# Optional playback and Windows shell features

The app remains radio-only. Podcast/RSS/download code was removed by product decision; Git retains it and no user profile data was deleted. All optional playback choices default off and persist in `playback-settings.json` next to the existing library. Writes are serialized and atomic; failed writes retain the durable values. Invalid or future-schema files remain untouched and disable these controls. Packaged launches import this file without overwriting existing package-local settings.

## Stream preparation

Settings → Playback → Prepare a saved stream on launch enables **native source preparation**, not just DNS/TLS priming. After startup, when no station is selected, the app considers up to eight saved recents/favorites and opens one direct HTTP(S) stream with a silent, non-autoplay MediaPlayer. It waits at most eight seconds for `MediaOpened`, then holds the prepared player/source for at most 30 seconds. Selecting the exact same URL transfers that player/source once to AudioService; the source is not recreated. A different selection, pause/stop, setting change, or shutdown cancels/retires speculative work. The feature skips SHOUTcast playlist entries and runs only on unrestricted connections with Energy Saver off and equalizer Off. It does not persist audio or send a Radio Browser play report, but the stream host can count the connection as a listener and it consumes network data.

Windows and the broadcaster control buffering; `MediaOpened` is not proof of a particular amount of buffered audio or a guaranteed latency improvement. Failed or expired preparation falls back to normal playback. Headless tests cover policy, cancellation, expiry, and ownership transfer. Live startup latency, listener accounting, network cancellation, and SMTC visibility still need measurement; no performance improvement is claimed yet.

## Finished broadcasts

Loop finished broadcasts reopens a completed source only when the setting is enabled, playback was still requested, and Windows reports at least one second of finite duration. Unknown-duration live EOF is not looped. A queued loop checks the current source and latest user intent, so a pause, stop, station switch, or disposal wins. Automatic looping, prewarming, and recovery do not submit play reports. Disabling looping takes effect at the next completion.

## Experimental equalizer

Speech, Bass, and Treble use a real Windows AudioGraph: one MediaSource audio input feeds the graph's four-band EqualizerEffectDefinition and one device output. The normal MediaPlayer path remains the default when Off. Positive gain is compensated by output headroom; presets are not loudness normalization, a limiter, or spatial audio.

The selected preset applies on the next stream open (including a resumed stream when changing engines). A source-less MediaPlayer provides manual system media controls without opening a second audio connection. ICY titles, station/song artwork, sleep timer, finite looping, default-output pause, and recovery remain connected. Opening is bounded to 12 seconds; graph errors and 30 seconds without position progress enter the existing bounded retry path. Late graph creation and native events are source/version-guarded, and teardown detaches handlers before disposing resources.

HLS timed-ID3 cues and speculative source preparation are unavailable on this experimental path. Native decoder/device availability can differ from MediaPlayer; some live sources may not expose useful position progress. Turn Off if a source repeatedly retries. The headless tests verify preset/headroom values, progress deadlines, settings, cancellation, and existing recovery logic, not audible equalizer behavior. Real ICY/HLS listening, system controls, output changes, pause/resume, CPU/memory, and decoder/resource cleanup are release gates.

## Jump lists

Settings → Playback → Show saved stations in Windows jump lists is opt-in and disabled in unsupported unpackaged builds. When enabled, packaged apps publish up to eight favorites/recent stations in the Windows taskbar/Start jump list. Favorite entries take precedence; duplicates, unsafe names, and oversized IDs are excluded. Updates serialize and coalesce; removed-by-user items are not republished during the current app session. Turning the choice off clears this app's custom station entries.

Shell arguments contain only a bounded escaped station ID, not a stream URL or developer key. Cold and redirected single-instance launches resolve that ID exclusively against the current local favorites/recents. An unknown/stale ID does nothing; opening an already-requested station does not pause it. The launch parser and playback intent paths have headless tests. Native removal persistence across restart, taskbar activation, and cold packaged launch still need MSIX QA.

## Optional SHOUTcast directory

Without configuration, Radio Browser is unchanged. Set `SHOUTKIT_SHOUTCAST_API_KEY` in the process environment before launching to enable a SHOUTcast fallback for empty/unavailable Radio Browser results. This differs from a provider-selection UI; no key is bundled, entered into Settings, written to profile files, or put into shell arguments. Obtain your own developer access and review provider terms before distribution; configuring a key does not imply approval to distribute it.

The provider uses the upstream iOS legacy top/search/genre endpoint shapes over HTTPS and resolves `shoutcast:` IDs through the keyless PLS tune-in endpoint immediately before playback. XML prohibits DTDs/external entities, bodies and result counts are bounded, text is strict UTF-8, keys/queries are escaped, redirects are disabled on the configured client, and directory exceptions discard potentially key-bearing transport messages. Only HTTP(S) playlist URLs without credentials/loopback are accepted. Playlist resolution is cancellable and selection-version guarded. SHOUTcast IDs are not Radio Browser UUIDs and do not contribute to its play telemetry. Synthetic tests cover directory/playlist parsing, limits, cancellation/fallback, and key redaction; a real configured-key smoke has not been performed.

## Damaged metadata

Valid UTF-8/Latin-1 ICY names and declared ID3 text encodings retain Unicode. A damaged ICY song cue clears the last accepted song and restores station-level UI/system metadata/artwork; repeated damaged cues invalidate once until a valid song arrives. Empty, advertising, and unsupported cues do not trigger that invalidation. Malformed recognized HLS title/artist text also clears the current song. Diagnostics record category/type/HResult only, never station URLs, artist/title text, or raw bytes. Previously saved listening history is not rewritten or guessed.

## Native validation checklist

1. Keep all options Off: play/switch/pause/resume KEXP and Brookdale, check titles, artwork, history, sleep timer, and system controls.
2. Enable preparation, restart without autoplay, select the warmed recent before 30 seconds, then select a different stream. Check no audio/media-card appears before selection, cancellation on close, and connection/resource cleanup. Repeat on a metered connection and with Energy Saver enabled.
3. Enable looping on a finite test broadcast; pause/stop/switch at completion. Confirm unknown-duration live EOF is not looped and automatic restarts do not send telemetry.
4. Enable each equalizer preset on a direct ICY stream. Confirm audible changes, controls and metadata, recovery during network loss, headphone removal, Off-to-DSP and DSP-to-Off transitions, and stable native memory across repeated switches.
5. Install a signed test MSIX and check favorites/recents jump lists, removal and restart, unknown IDs, cold launch, and redirected launch.
6. With an authorized developer key, force a directory fallback and validate SHOUTcast search, tune-in, station switching during resolution, and sanitized failures. Do not log the key or request URL.

Platform references: [MediaPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer), [AudioGraph](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/audio-graphs), [Windows jump lists](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/jump-list), and [SHOUTcast developer access](https://directory.shoutcast.com/Developer). Existing signing, accessibility, and provenance gates remain in [RELEASE.md](RELEASE.md).
