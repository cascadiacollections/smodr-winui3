using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using smodr.Models;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("DiagnosticScenario")]
public sealed class RuntimeDiagnosticWiringTests
{
    [TestMethod]
    public async Task ScriptedRetryBudgetRecordsRequestsAndExhaustionExactly()
    {
        var counters = new RuntimeDiagnosticCounters();
        var clock = new FakeTimeProvider();
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = new LiveRadioRecovery(_ => restarted.TrySetResult(), _ => { }, clock,
            retryBaseDelay: TimeSpan.FromSeconds(1), maxRetries: 1, diagnostics: counters);
        recovery.Begin();
        recovery.Fail();
        clock.Advance(TimeSpan.FromSeconds(1));
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        recovery.Fail();
        recovery.Pause();
        recovery.Fail();
        AssertCount(counters, RuntimeCounter.RecoveryRetryScheduled, 1);
        AssertCount(counters, RuntimeCounter.RecoveryRestartRequested, 1);
        AssertCount(counters, RuntimeCounter.RecoveryExhausted, 1);
    }

    [TestMethod]
    public async Task ScriptedIcyCuesRecordOnlyFixedAcceptanceAndRejectionCategories()
    {
        var counters = new RuntimeDiagnosticCounters();
        var reader = new Reader();
        using var monitor = new IcyTrackMonitor(new Probe(), continuousReader: reader, diagnostics: counters);
        monitor.Start(new RadioStation { Name = "Synthetic Radio", StreamUrl = "https://station.example/private" });
        var emit = await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        emit(string.Empty);
        emit(new string('x', 4097));
        emit("StreamTitle='Artist - Commercial break';");
        emit("StreamTitle='Artist - S\uFFFDng';");
        emit("StreamTitle='Artist - S\uFFFDng';");
        emit("StreamTitle='Artist - Song';");
        emit("StreamTitle='Artist - Song';");
        monitor.Stop();
        emit("StreamTitle='Artist - Retired';");
        await monitor.ShutdownAsync();
        foreach (var counter in new[]
                 {
                     RuntimeCounter.MetadataRejectedEmpty, RuntimeCounter.MetadataRejectedOversize,
                     RuntimeCounter.MetadataRejectedNonSong, RuntimeCounter.MetadataRejectedDamaged,
                     RuntimeCounter.MetadataAccepted, RuntimeCounter.MetadataDuplicate,
                     RuntimeCounter.MetadataRetired
                 })
        {
            AssertCount(counters, counter, 1);
        }

        Assert.IsFalse(counters.Snapshot().Keys.Any(key => key.Contains("Artist", StringComparison.Ordinal)
                                                           || key.Contains("station.example",
                                                               StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CacheCountersDistinguishHitsMissesAndMonotonicExpiry()
    {
        var counters = new RuntimeDiagnosticCounters();
        var clock = new FakeTimeProvider();
        var cache = new ArtworkMemoryCache<string>(2, 10, clock, counters);
        Assert.IsFalse(cache.TryGet("private-url", out _));
        cache.Put("private-url", "image", 5);
        Assert.IsTrue(cache.TryGet("private-url", out _));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.IsFalse(cache.TryGet("private-url", out _));
        AssertCount(counters, RuntimeCounter.ArtworkMemoryHit, 1);
        AssertCount(counters, RuntimeCounter.ArtworkMemoryMiss, 2);
        AssertCount(counters, RuntimeCounter.ArtworkMemoryExpired, 1);
    }

    [TestMethod]
    public async Task CatalogCountersDistinguishSharedRequestNegativeCacheMatchAndRejectedResponse()
    {
        var counters = new RuntimeDiagnosticCounters();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new ControlledHttpHandler(async (_, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                await release.Task.WaitAsync(token);
            }

            var json = call == 2
                ? "{\"results\":[{\"artistName\":\"Artist\",\"trackName\":\"Second\",\"artworkUrl100\":\"https://is1-ssl.mzstatic.com/a/100x100bb.jpg\"}]}"
                : "{\"results\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (call == 3)
            {
                response.Content.Headers.ContentLength = 999;
            }

            return response;
        });
        using var client = new HttpClient(handler);
        await using var catalog = new AlbumArtworkLookup(client, diagnostics: counters);
        var track = new RadioTrackInfo("First", "Artist");
        var first = catalog.FindAsync(track);
        var shared = catalog.FindAsync(track);
        release.TrySetResult();
        Assert.IsNull(await first);
        Assert.IsNull(await shared);
        Assert.IsNull(await catalog.FindAsync(track));
        Assert.IsNotNull(await catalog.FindAsync(new RadioTrackInfo("Second", "Artist")));
        Assert.IsNull(await catalog.FindAsync(new RadioTrackInfo("Third", "Artist")));
        AssertCount(counters, RuntimeCounter.AlbumLookupStarted, 3);
        AssertCount(counters, RuntimeCounter.AlbumLookupJoined, 1);
        AssertCount(counters, RuntimeCounter.AlbumCacheHit, 1);
        AssertCount(counters, RuntimeCounter.AlbumMatch, 1);
        AssertCount(counters, RuntimeCounter.AlbumMiss, 1);
        AssertCount(counters, RuntimeCounter.AlbumResponseRejected, 1);
    }

    [TestMethod]
    public async Task ArtworkDeadlineAndRejectedContentAreNotCountedAsTransportFailures()
    {
        var counters = new RuntimeDiagnosticCounters();
        using var body = new ControlledHttpBody([], true);
        var calls = 0;
        using var handler = new ControlledHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref calls) == 1
                    ? new StreamContent(body)
                    : new ByteArrayContent([1])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(calls == 1 ? "image/png" : "text/html");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await using var loader =
            new StationArtworkLoader(client, timeout: TimeSpan.FromMilliseconds(100), diagnostics: counters);
        var pending = loader.GetAsync(new Uri("https://art.example/stalled"));
        await body.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsNull(await loader.GetAsync(new Uri("https://art.example/html")));
        AssertCount(counters, RuntimeCounter.ArtworkTransportCanceled, 1);
        AssertCount(counters, RuntimeCounter.ArtworkResponseRejected, 1);
        AssertCount(counters, RuntimeCounter.ArtworkTransportFailed, 0);
        AssertCount(counters, RuntimeCounter.ArtworkMemoryMiss, 4);
    }

    private static void AssertCount(RuntimeDiagnosticCounters counters, RuntimeCounter counter, long expected)
    {
        Assert.AreEqual(expected, counters.Snapshot()[counter.ToString()], counter.ToString());
    }

    private sealed class Reader : IContinuousTrackMetadataReader
    {
        public TaskCompletionSource<Action<string>> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> ListenAsync(Uri streamUri, Action<string> onMetadata,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(onMetadata);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }
    }

    private sealed class Probe : ITrackMetadataProbe
    {
        public Task<IcyProbeResult> ProbeAsync(Uri streamUri, CancellationToken cancellationToken = default)
        {
            throw new AssertFailedException("Continuous reader should not fall through to a probe.");
        }
    }
}
