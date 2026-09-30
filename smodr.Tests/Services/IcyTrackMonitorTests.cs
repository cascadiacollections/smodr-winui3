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
}
