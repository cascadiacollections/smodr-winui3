using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using smodr.Models;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("Soak")]
public sealed class RadioRuntimeSoakTests
{
    [TestMethod]
    public async Task FiveHundredReconnectCyclesRetireEveryEngineAndLeaveNoPostShutdownRetry()
    {
        var clock = new FakeTimeProvider();
        var retries = Channel.CreateUnbounded<long>();
        var queued = new Queue<Action>();
        using var coordinator = new RadioAudioEngineCoordinator(queued.Enqueue);
        using var recovery = new LiveRadioRecovery(epoch => retries.Writer.TryWrite(epoch),
            _ => Assert.Fail("Unexpected exhaustion"), clock);
        var engines = new List<TestRadioEngine>();
        var delivered = 0;
        coordinator.Failed += (_, _) => delivered++;
        for (var cycle = 0; cycle < 500; cycle++)
        {
            var engine =
                new TestRadioEngine(cycle % 2 == 0
                    ? RadioAudioEngineKind.MediaPlayer
                    : RadioAudioEngineKind.AudioGraph);
            engines.Add(engine);
            coordinator.Replace(engine);
            recovery.Begin();
            engine.EmitFailed();
            recovery.Fail();
            coordinator.Replace(null); // Production retires the failed stream before retry.
            clock.Advance(TimeSpan.FromSeconds(2));
            var epoch = await retries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(recovery.IsCurrent(epoch));
            recovery.Playing();
            while (queued.TryDequeue(out var callback))
            {
                callback();
            }
        }

        Assert.AreEqual(0, delivered);
        Assert.IsTrue(engines.All(engine => engine.Disposals == 1));
        recovery.Dispose();
        coordinator.Dispose();
        clock.Advance(TimeSpan.FromDays(1));
        Assert.IsFalse(retries.Reader.TryRead(out _));
        Assert.IsNull(coordinator.Current);
    }

    [TestMethod]
    public void ConcurrentArtworkChurnRemainsWithinCountAndWeightBoundsAndExpires()
    {
        var clock = new FakeTimeProvider();
        var cache = new ArtworkMemoryCache<byte[]>(32, 65_536, clock);
        Parallel.For(0, 5000, index =>
        {
            cache.Put($"cover:{index}", new byte[4096], 4096);
            cache.TryGet($"cover:{Math.Max(0, index - 1)}", out _);
        });
        var retained = 0;
        long bytes = 0;
        for (var index = 0; index < 5000; index++)
        {
            if (!cache.TryGet($"cover:{index}", out var cover))
            {
                continue;
            }

            retained++;
            bytes += cover!.Length;
        }

        Assert.IsTrue(retained is > 0 and <= 32);
        Assert.IsTrue(bytes <= 65_536);
        clock.Advance(TimeSpan.FromMinutes(30));
        for (var index = 0; index < 5000; index++)
        {
            Assert.IsFalse(cache.TryGet($"cover:{index}", out _));
        }
    }

    [TestMethod]
    public async Task HistoryChurnAndLateArtworkStayBoundedAcrossReload()
    {
        var directory = Directory.CreateTempSubdirectory("shoutkit-history-soak-");
        try
        {
            var file = Path.Combine(directory.FullName, "history.json");
            var history = new TrackHistoryService(file, 32);
            var station = new RadioStation { Id = "synthetic", Name = "Synthetic radio" };
            var artwork = new AlbumArtworkMatch(new Uri("https://example.com/cover.png"), null);
            Guid oldest = default;
            for (var batch = 0; batch < 16; batch++)
            {
                var writes = Enumerable.Range(batch * 16, 16)
                    .Select(index => history.RecordAsync(station, new RadioTrackInfo($"Song {index}", "Artist")))
                    .ToArray();
                var ids = await Task.WhenAll(writes);
                if (batch == 0)
                {
                    oldest = ids[0];
                }

                await history.UpdateArtworkAsync(ids[^1], artwork);
                Assert.IsTrue(history.Entries.Count <= 32);
            }

            await history.UpdateArtworkAsync(oldest, artwork); // Evicted entry must not be resurrected.
            await history.FlushAsync();
            var reloaded = new TrackHistoryService(file, 32);
            Assert.HasCount(32, reloaded.Entries);
            Assert.HasCount(32, reloaded.Entries.Select(entry => entry.Id).Distinct());
            Assert.AreEqual("Song 255", reloaded.Entries[0].Title);
            Assert.IsFalse(reloaded.Entries.Any(entry => entry.Id == oldest));
            Assert.IsTrue(new FileInfo(file).Length < 2_000_000);
        }
        finally { directory.Delete(true); }
    }

    [TestMethod]
    [TestCategory("Loopback")]
    public async Task RepeatedRealSocketReconnectsReleaseResponsesAndServerConnections()
    {
        await using var server = new SyntheticRadioServer();
        using var handler = new HttpClientHandler { UseProxy = false, MaxConnectionsPerServer = 1 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var reader = new IcyMetadataStreamReader(client);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var cues = 0;
            Assert.IsTrue(await reader.ListenAsync(server.UriFor(iteration % 2 == 0 ? "icy" : "truncated"), _ => cues++)
                .WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(iteration % 2 == 0 ? 2 : 1, cues);
        }

        Assert.AreEqual(128, server.Requests);
        // Disposal awaits the listener and handlers. One connection slot would stall if responses leaked.
    }
}
