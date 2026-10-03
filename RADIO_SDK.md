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

The app is the first real consumer, not a separate duplicate implementation. Its existing interfaces map SDK wire stations into presentation models. Existing smodr.Models/Services namespaces are deliberately retained in extracted domain/metadata/service modules to keep this migration safe; **those APIs are pre-release and not frozen**. The clean RadioBrowser namespace is independent of app models.

## Transport and API policy

Consumer identity is required in ClientOptions.UserAgent, attached per request without modifying shared HTTP defaults. Supply separate non-retrying HTTP clients for writes. The library owns neither HTTP clients nor mirror providers. Configure TimeProvider/discovery delegates for deterministic tests and optional IClientDiagnostics for fixed-category events, with no listener data or disk IO.

Read responses have a 2 MiB ceiling, 1,000 scanned rows, at most 100 returned stations and an eight-second default **per-mirror** deadline covering headers and body. Strict advertised-length validation rejects truncation; invalid/malformed/oversize responses fail over. Caller cancellation stops failover. Pages reject invalid limits/offsets. The SDK does not sanitize or download station artwork/homepage URLs; hosts own that security policy.

DNS discovery coalesces refreshes, filters official HTTPS mirror roots, caches for six hours, randomizes each returned snapshot and retains known mirrors during outages. Failed refresh backs off one minute; the official aggregate hostname is the first-launch fallback. One canceled waiter does not cancel discovery for others.

Search/rank/tag/UUID/URL reads never report a click. RegisterClickAsync and VoteAsync require explicit caller action and consent. They use one mirror, with no SDK retries or failover. Injected HTTP retry/redirect handlers remain the host's responsibility. **App compatibility adapter retains its previous best-effort mirror failover for reports; ambiguous failures can duplicate reports.** Neither layer promises exactly-once delivery.

Not every administrative Radio Browser endpoint is implemented: station edits, checks, change feeds and server administration remain out of scope. This is an independent MIT implementation, not code copied from RadioBrowser.NET and not a drop-in replacement.

## Verification and versioning

Cascadia.Radio.Tests targets .NET 10 without the app, WinUI or Windows SDK. Selected existing tests are linked, not copied, so Windows integration and portable runs share the same assertions. They cover parser fuzzing, metadata lifecycle, bounded transport/artwork, concurrent persistence, and direct SDK contracts. The root Windows solution retains its integration suite.

SDK packages are local pre-release 0.1.0 artifacts. Package validation is enabled. Before public release, freeze clean domain/service namespaces, add full XML documentation, choose package identities, establish a released compatibility baseline and independently review the public API. Nothing here publishes packages or pushes Git changes.
