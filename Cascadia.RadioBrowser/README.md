# Cascadia.RadioBrowser (pre-release source)
Independent MIT .NET 10 client. Not a drop-in RadioBrowser.NET replacement.
Supply an HttpClient, IMirrorProvider, and ClientOptions with your application's UserAgent.
Reads use randomized DNS mirrors with bounded responses, whole-request deadlines, caller cancellation and failover. SearchOptions exposes pagination, ranking and filters. Station wire data is separate from app presentation.
RegisterClickAsync and VoteAsync are explicit mutations: no automatic telemetry or mutation failover. Supply a non-retrying write client; an injected retry handler can defeat this guarantee. The caller owns consent.
Source-generated JSON; trim/AOT compatible. No disk logging, Windows dependencies, or ownership of supplied clients/providers.
Search, rankings, tags, UUID/URL lookup and directory facets are supported. Station editing, check history, change feeds and server administration are intentionally not implemented.
No package has been published.
