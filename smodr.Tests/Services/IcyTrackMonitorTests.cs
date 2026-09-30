using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class IcyTrackMonitorTests
{
    [TestMethod]
    public async Task StaleProbeCannotPublishAfterStationSwitch()
    {
        var oldResult = new TaskCompletionSource<IcyProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newPublished = new TaskCompletionSource<RadioTrackUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StubProbe(uri => uri.AbsolutePath == "/old"
            ? oldResult.Task
            : Task.FromResult(new IcyProbeResult(true, "StreamTitle='New Artist - New Song';")));
        using var monitor = new IcyTrackMonitor(probe);
        var updates = new List<RadioTrackUpdate>();
        monitor.TrackChanged += (_, update) =>
        {
            lock (updates) updates.Add(update);
            newPublished.TrySetResult(update);
        };

        monitor.Start(Station("old"));
        monitor.Start(Station("new"));
        oldResult.SetResult(new IcyProbeResult(true, "StreamTitle='Old Artist - Old Song';"));
        var published = await newPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Stop();

        Assert.AreEqual("New Song", published.Track.Title);
        lock (updates) Assert.HasCount(1, updates);
    }

    [TestMethod]
    public async Task SamplesPlayingIcyStreamEveryTenSeconds()
    {
        var nextDelay = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new IcyTrackMonitor(
            new StubProbe(_ => Task.FromResult(new IcyProbeResult(true, "StreamTitle='Artist - Song';"))),
            (duration, token) =>
            {
                nextDelay.TrySetResult(duration);
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        monitor.Start(Station("one"));
        var interval = await nextDelay.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(interval > TimeSpan.FromSeconds(9) && interval <= TimeSpan.FromSeconds(10));
        monitor.Stop();
    }

    [TestMethod]
    public async Task ProbeDurationCountsTowardSampleCadence()
    {
        var clock = new FakeClock();
        var nextDelay = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new IcyTrackMonitor(new StubProbe(_ =>
        {
            clock.Advance(TimeSpan.FromSeconds(7));
            return Task.FromResult(new IcyProbeResult(true, "StreamTitle='Artist - Song';"));
        }), (duration, token) =>
        {
            nextDelay.TrySetResult(duration);
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, clock);
        monitor.Start(Station("one"));
        Assert.AreEqual(TimeSpan.FromSeconds(3), await nextDelay.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        monitor.Stop();
    }

    [TestMethod]
    public async Task ContinuousReaderPublishesRapidChangesWithoutPolling()
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCalls = 0;
        using var monitor = new IcyTrackMonitor(new StubProbe(_ =>
        {
            Interlocked.Increment(ref probeCalls);
            return Task.FromResult(IcyProbeResult.Unsupported);
        }), continuousReader: new StubContinuousReader((_, emit, token) =>
        {
            emit("StreamTitle='Artist - First';");
            emit("StreamTitle='Artist - Second';");
            return Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith(_ => true);
        }));
        var titles = new List<string>();
        monitor.TrackChanged += (_, update) =>
        {
            lock (titles)
            {
                titles.Add(update.Track.Title);
                if (titles.Count == 2) published.TrySetResult();
            }
        };
        monitor.Start(Station("one"));
        await published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        monitor.Stop();
        lock (titles)
        {
            Assert.HasCount(2, titles);
            Assert.AreEqual("First", titles[0]);
            Assert.AreEqual("Second", titles[1]);
        }
        Assert.AreEqual(0, probeCalls);
    }

    [TestMethod]
    public async Task FailedContinuousReaderFallsBackToBoundedProbe()
    {
        var published = new TaskCompletionSource<RadioTrackUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new IcyTrackMonitor(
            new StubProbe(_ => Task.FromResult(new IcyProbeResult(true,
                "StreamTitle='Artist - Fallback';"))),
            continuousReader: new StubContinuousReader((_, _, _) => throw new IOException("stream ended")));
        monitor.TrackChanged += (_, update) => published.TrySetResult(update);
        monitor.Start(Station("one"));
        Assert.AreEqual("Fallback", (await published.Task.WaitAsync(TimeSpan.FromSeconds(3))).Track.Title);
        monitor.Stop();
    }

    [TestMethod]
    public async Task StoppedContinuousReaderCannotPublishLateCue()
    {
        Action<string>? delayedEmit = null;
        var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new IcyTrackMonitor(
            new StubProbe(_ => Task.FromResult(IcyProbeResult.Unsupported)),
            continuousReader: new StubContinuousReader(async (_, emit, token) =>
            {
                delayedEmit = emit;
                readerStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            }));
        var updates = 0;
        monitor.TrackChanged += (_, _) => Interlocked.Increment(ref updates);
        monitor.Start(Station("one"));
        await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        monitor.Stop();
        delayedEmit!("StreamTitle='Artist - Late';");
        Assert.AreEqual(0, updates);
    }

    [TestMethod]
    public async Task ContinuousReaderRetriesAfterFallbackInterval()
    {
        var clock = new FakeClock();
        var published = new TaskCompletionSource<RadioTrackUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var monitor = new IcyTrackMonitor(
            new StubProbe(_ => Task.FromResult(new IcyProbeResult(true, null))),
            (duration, _) =>
            {
                clock.Advance(duration);
                return Task.CompletedTask;
            }, clock,
            new StubContinuousReader(async (_, emit, token) =>
            {
                if (Interlocked.Increment(ref attempts) == 1) throw new IOException("temporary drop");
                emit("StreamTitle='Artist - Recovered';");
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            }));
        monitor.TrackChanged += (_, update) => published.TrySetResult(update);
        monitor.Start(Station("one"));
        Assert.AreEqual("Recovered", (await published.Task.WaitAsync(TimeSpan.FromSeconds(3))).Track.Title);
        Assert.AreEqual(2, attempts);
        monitor.Stop();
    }

    private static RadioStation Station(string id) => new()
    {
        Id = id,
        Name = id,
        StreamUrl = $"https://example.com/{id}"
    };

    private sealed class StubProbe(Func<Uri, Task<IcyProbeResult>> get) : ITrackMetadataProbe
    {
        public Task<IcyProbeResult> ProbeAsync(Uri streamUri,
            CancellationToken cancellationToken = default) => get(streamUri);
    }

    private sealed class StubContinuousReader(
        Func<Uri, Action<string>, CancellationToken, Task<bool>> listen) : IContinuousTrackMetadataReader
    {
        public Task<bool> ListenAsync(Uri streamUri, Action<string> onMetadata,
            CancellationToken cancellationToken = default) => listen(streamUri, onMetadata, cancellationToken);
    }

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref _ticks, duration.Ticks);
            _now += duration;
        }
    }
}
