# Synthetic stream reliability checks

The `Loopback` test category starts a per-test HTTP server on `127.0.0.1` with an OS-assigned ephemeral port. It uses TcpListener rather than HTTP.sys, so it needs no URL reservation, administrator setup or firewall change. Every server, connection and cancellation source is closed during test cleanup. Clients explicitly bypass proxies. No public station, directory API, developer key, profile data, UI window or audio device is used.

Fixture routes:

| Route | Purpose |
| --- | --- |
| `/icy` | UTF-8 ICY title changes written in three-byte fragments |
| `/redirect` | Relative HTTP redirect to the ICY source |
| `/truncated` | Disconnect during the second metadata block |
| `/damaged` | Valid title, replacement-character cue, then a new valid title |
| `/stall` | Send headers, then stop body progress until cancellation |
| `/recover` | Two HTTP 503 failures followed by a readable stream |
| `/wav` | One-second, mono, 8 kHz, 16-bit silent PCM WAV |

Tests run the production ICY probe/continuous reader and title parser over actual HTTP sockets. The recovery scenario combines real transport failures with fake-time retry policy so exponential delays are deterministic. No partial cue may be published, user cancellation must terminate a stalled read, and another connection must remain usable. The fixture's ICY audio bytes are opaque protocol filler, not a decodable MP3. The finite WAV is structurally checked but is not played.

Run after a Release build on Windows:

```powershell
dotnet test smodr.slnx -c Release --no-build --no-restore -p:Platform=ARM64 --filter "TestCategory=Loopback"
```

Use `Platform=x64` on x64. The normal test run includes this category; CI repeats it three additional times on each Windows architecture. These are headless transport/policy checks, **not** proof of MediaPlayer/AudioGraph audible playback, native buffering, HLS decoding, output-device behavior, SMTC or resource usage. Those remain the native checklist in [PLAYBACK_FEATURES.md](PLAYBACK_FEATURES.md) and [RELEASE.md](RELEASE.md). Live-stream smoke tests remain a separate opt-in workflow.
