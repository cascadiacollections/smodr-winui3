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

    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}
