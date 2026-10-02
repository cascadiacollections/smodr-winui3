# Radio Browser API usage audit

Source review dated 2026-10-02 against the [official client recommendations](https://api.radio-browser.info/) and [endpoint reference](https://de1.api.radio-browser.info/). The upstream iOS checkout inspected was `shoutkit` at `a57dd0b78a8dbc4fc7fd8dd7bf1c508b1d4db697`. This review did not modify upstream, run iOS tests, or send live play reports. Findings describe source behavior, not observed production failures.

## Guidance and response-contract gaps in upstream iOS

Paths below are relative to the upstream checkout, not this Windows repository.

| Area | Source-confirmed iOS gap | Windows adaptation in this change |
| --- | --- | --- |
| Mirror selection | Fixed aggregate/de1 list, always starting at index zero; no application DNS discovery or shuffle | Cached forward/reverse DNS discovery, per-operation shuffle, sequential failover, stale-cache/aggregate fallback |
| Country metadata | Response DTO and mapping use deprecated `country`; search/geo filters already use `countrycode` | Directory mapping uses `countrycode` with localized display; legacy persisted names remain readable |
| Explicit user resume | `play(_:)` emits the reporting callback but `resume()` does not, including remote media Play | In-app and Windows media-control explicit resume report when enabled; automatic recovery stays silent |
| Report result | HTTP-success body discarded without checking JSON `ok` | Validate string or boolean true in a bounded response; fail over on rejection/malformed data |
| User-Agent version | Descriptive headers exist, but production hardcodes `Holmdel/0.1` and default is `ShoutKit/0.1` | App identity includes generated assembly version |

Evidence anchors:

- Mirror defaults/order: `Packages/RadioDirectory/Sources/RadioDirectory/RadioBrowserDirectoryClient.swift:20`, `:260`, `:275`.
- Country DTO: `Packages/RadioDirectory/Sources/RadioDirectory/RadioBrowserWireTypes.swift:13`; mapping: `RadioBrowserDirectoryClient+Mapping.swift:37`, `:59` in the same source directory. Correct query usage: `RadioBrowserDirectoryClient+Filters.swift:20` and `RadioBrowserGeoFilter.swift:37`.
- Play/resume: `Packages/Playback/Sources/Playback/PlaybackController.swift:253`, `:299`, `:325`; deliberate resume suppression and remote binding: `PlaybackController+Internals.swift:15`, `:370` in the same source directory. Reporting callback: `HolmdelApp/Holmdel/AppDependencies+Callbacks.swift:31`.
- Discarded report response: `Packages/RadioDirectory/Sources/RadioDirectory/RadioBrowserDirectoryClient.swift:206`; HTTP-status-only success: `HTTPTransport.swift:314` in the same directory.
- User-Agent: `HolmdelApp/Holmdel/AppDependencies+Factories.swift:109`; reusable default/header: `Packages/RadioDirectory/Sources/RadioDirectory/RadioBrowserDirectoryClient.swift:30`, `:279`. Whether `0.1` matches the shipped version was not verified.

The API requests click reporting whenever a user starts a stream and deduplicates counts for the same IP/station within a day. Resume suppression is therefore unnecessary for count deduplication. Use a separate explicit-user-start callback upstream rather than expanding a callback that also updates recents. Checking `ok` is response-contract correctness, not a separate client-etiquette rule. Keep reporting optional and nonblocking.

## Optional upstream hardening

These are robustness opportunities, not additional official API violations:

- Bound buffered directory/report bodies and the whole operation. `Packages/RadioDirectory/Sources/RadioDirectory/HTTPTransport.swift:202` buffers the complete response; the five-second request timeout at `ShoutcastDirectoryClient.swift:210` is not proof of a whole-operation deadline.
- Classify transient retry conditions rather than retrying every non-cancellation error; consider `Retry-After`. See `HTTPTransport.swift:335`, `:350` and `RadioBrowserDirectoryClient.swift:265`.
- Fail over on malformed successful directory JSON: decoding happens after transport failover returns at `RadioBrowserDirectoryClient.swift:234`.
- Track directory provenance separately from syntactically valid UUIDs: `RadioBrowserDirectoryClient.swift:207` validates the UUID shape, not its origin. External caller-supplied station IDs should not gain telemetry trust automatically.

All abbreviated source filenames in this section are under `Packages/RadioDirectory/Sources/RadioDirectory/`.

## Already aligned upstream

Station identities use `stationuuid`, lookups use `/json/stations/byuuid`, and playback prefers `url_resolved` with `url` fallback. A persisted privacy choice gates reporting (`HolmdelApp/Holmdel/AppDependencies+Callbacks.swift:37`), non-UUID IDs are skipped, client-identifying headers and bounded retry counts exist, HTTP failures advance to another configured host, and internal recovery does not emit extra reports.

## Windows verification and remaining release checks

228 deterministic tests pass on ARM64 and x64 with warnings treated as errors. New coverage exercises discovery filtering/cache refresh, DNS outage fallback/backoff, cancellation, rejected report failover, country-code mapping, explicit resumes, and suppression for untrusted links. Formatting checks pass; the self-contained ARM64 preview and unsigned MSIX build successfully. Live DNS/stream behavior on corporate networks, interactive media controls/privacy text, installed-package QA, signing, and release-time dependency auditing remain release checks; this is not a claim of completed production certification.
