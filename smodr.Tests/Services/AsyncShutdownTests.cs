using System.Net;
using System.Net.Http.Headers;
using smodr.Models;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

[TestClass]
public sealed class AsyncShutdownTests
{
    [TestMethod]
    public async Task AlbumShutdownCancelsBodyReadAndCompletesAllSharedListeners()
    {
        using var body = new ControlledHttpBody([], stalled: true);
        using var handler = new ControlledHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await using var lookup = new AlbumArtworkLookup(client);
        var first = lookup.FindAsync(new RadioTrackInfo("Song", "Artist"));
        var second = lookup.FindAsync(new RadioTrackInfo("Song", "Artist"));
        await body.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await lookup.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(await first);
        Assert.IsNull(await second);
        Assert.IsTrue(body.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lookup.FindAsync(new RadioTrackInfo("New", "Artist")));
    }

    [TestMethod]
    public async Task MetadataShutdownDrainsRetiredProbeAndSuppressesItsLateTitle()
    {
        var probeResult = new TaskCompletionSource<IcyProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new IcyTrackMonitor(new Probe(() => { started.TrySetResult(); return probeResult.Task; }));
        var published = 0;
        monitor.TrackChanged += (_, _) => published++;
        monitor.Start(new RadioStation { Name = "Synthetic", StreamUrl = "https://example.com/live" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        monitor.Stop();
        var shutdown = monitor.ShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted);
        probeResult.SetResult(new IcyProbeResult(true, "StreamTitle='Artist - Late';"));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, published);
    }

    [TestMethod]
    public async Task PrewarmShutdownWaitsForFactoryCleanupWithoutOpeningAudio()
    {
        var directory = Directory.CreateTempSubdirectory("shoutkit-warm-shutdown-");
        try
        {
            var preferences = new RadioPlaybackPreferences(Path.Combine(directory.FullName, "settings.json"));
            await preferences.UpdateAsync(value => value with { PrewarmStreams = true });
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaned = false;
            using var warmer = new RadioStreamPrewarmer(preferences, () => true, async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; }
                finally { cleaned = true; }
            });
            var pending = warmer.WarmAsync((RadioStation[])[new RadioStation { StreamUrl = "https://example.com/live" }]);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await warmer.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(3));
            await pending;
            Assert.IsTrue(cleaned);
            await warmer.WarmAsync((RadioStation[])[new RadioStation { StreamUrl = "https://example.com/late" }]);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class Probe(Func<Task<IcyProbeResult>> read) : ITrackMetadataProbe
    {
        public Task<IcyProbeResult> ProbeAsync(Uri streamUri, CancellationToken cancellationToken = default) => read();
    }
}
