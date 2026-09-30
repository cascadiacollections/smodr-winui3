using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class TrackHistoryServiceTests
{
    [TestMethod]
    public async Task ConsecutiveDuplicateRefreshesTimestampAndSurvivesRestart()
    {
        var file = TempFile();
        try
        {
            var clock = new FakeTimeProvider();
            var history = new TrackHistoryService(file, clock: clock);
            var station = Station("one");
            var track = new RadioTrackInfo("Song", "Artist");
            await history.RecordAsync(station, track);
            var first = history.Entries[0].HeardAt;
            clock.Advance(TimeSpan.FromMinutes(1));
            await history.RecordAsync(station, track);
            Assert.HasCount(1, history.Entries);
            Assert.IsTrue(history.Entries[0].HeardAt > first);
            Assert.HasCount(1, new TrackHistoryService(file).Entries);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task ConcurrentWritesAreOrderedAndBounded()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file, limit: 10);
            var station = Station("one");
            var writes = Enumerable.Range(0, 40)
                .Select(index => history.RecordAsync(station, new RadioTrackInfo($"Song {index}", null)));
            await Task.WhenAll(writes);
            await history.FlushAsync();
            Assert.HasCount(10, history.Entries);
            Assert.AreEqual("Song 39", history.Entries[0].Title);
            Assert.HasCount(10, new TrackHistoryService(file, limit: 10).Entries);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task InterleavedTracksRemainSeparateAndUnnamedIdsUseStationName()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file);
            var first = Station(string.Empty);
            var second = new RadioStation
            {
                Id = string.Empty,
                Name = "Radio Two",
                StreamUrl = "https://example.com/other"
            };
            var track = new RadioTrackInfo("Song", "Artist");
            await history.RecordAsync(first, track);
            await history.RecordAsync(second, track);
            await history.RecordAsync(first, track);
            Assert.HasCount(3, history.Entries);
            Assert.AreEqual("Radio One", history.Entries[0].StationName);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task CorruptFileIsPreserved()
    {
        var file = TempFile();
        try
        {
            await File.WriteAllTextAsync(file, "{ broken");
            var history = new TrackHistoryService(file);
            await Assert.ThrowsAsync<IOException>(() => history.RecordAsync(Station("one"),
                new RadioTrackInfo("Song", null)));
            Assert.AreEqual("{ broken", await File.ReadAllTextAsync(file));
        }
        finally { File.Delete(file); }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"shoutkit-tracks-{Guid.NewGuid():N}.json");

    private static RadioStation Station(string id) => new()
    {
        Id = id,
        Name = "Radio One",
        StreamUrl = "https://example.com/live"
    };

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
