using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class TrackHistoryServiceTests
{
    [TestMethod]
    public async Task ExplicitArrivalTimeSurvivesPersistenceAndDuplicateClockReversal()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file);
            var arrival = DateTimeOffset.Parse("2026-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            await history.RecordAtAsync(Station("one"), new RadioTrackInfo("Song", "Artist"), arrival);
            await history.RecordAtAsync(Station("one"), new RadioTrackInfo("Song", "Artist"), arrival.AddMinutes(-1));
            Assert.AreEqual(arrival, new TrackHistoryService(file).Entries[0].HeardAt);
        }
        finally { File.Delete(file); }
    }
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

    [TestMethod]
    public async Task ArtworkUpdateSurvivesRestartAndCannotOverwriteAnotherTrack()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file);
            var station = Station("one");
            var oldTrack = new RadioTrackInfo("Old", "Artist");
            var currentTrack = new RadioTrackInfo("Current", "Artist");
            await history.RecordAsync(station, oldTrack);
            var currentId = await history.RecordAsync(station, currentTrack);
            var match = new AlbumArtworkMatch(new Uri("https://is1-ssl.mzstatic.com/current.jpg"),
                new Uri("https://music.apple.com/current"));
            await history.UpdateArtworkAsync(currentId, match);

            var reloaded = new TrackHistoryService(file).Entries;
            Assert.AreEqual(match.ArtworkUrl.AbsoluteUri, reloaded[0].ArtworkUrl);
            Assert.AreEqual(match.StoreUrl?.AbsoluteUri, reloaded[0].AppleMusicUrl);
            Assert.AreEqual(string.Empty, reloaded[1].ArtworkUrl);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task ConcurrentRecordThenArtworkUpdateKeepsLatestHistory()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file);
            var station = Station("one");
            var track = new RadioTrackInfo("Song", "Artist");
            var record = history.RecordAsync(station, track);
            async Task UpdateAsync() => await history.UpdateArtworkAsync(await record,
                new AlbumArtworkMatch(new Uri("https://is1-ssl.mzstatic.com/song.jpg"), null));
            var artwork = UpdateAsync();
            await Task.WhenAll(record, artwork);
            Assert.AreEqual("https://is1-ssl.mzstatic.com/song.jpg", history.Entries[0].ArtworkUrl);
            Assert.AreEqual(history.Entries[0].ArtworkUrl, new TrackHistoryService(file).Entries[0].ArtworkUrl);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task RepeatedSongArtworkTargetsOnlyItsOwnOccurrence()
    {
        var file = TempFile();
        try
        {
            var history = new TrackHistoryService(file);
            var station = Station("one");
            var song = new RadioTrackInfo("Song", "Artist");
            var earlierId = await history.RecordAsync(station, song);
            await history.RecordAsync(station, new RadioTrackInfo("Another", "Artist"));
            var laterId = await history.RecordAsync(station, song);
            Assert.AreNotEqual(earlierId, laterId);
            await history.UpdateArtworkAsync(earlierId,
                new AlbumArtworkMatch(new Uri("https://is1-ssl.mzstatic.com/earlier.jpg"), null));
            Assert.AreEqual(string.Empty, history.Entries[0].ArtworkUrl);
            Assert.AreEqual("https://is1-ssl.mzstatic.com/earlier.jpg", history.Entries[2].ArtworkUrl);
            Assert.AreEqual(earlierId, new TrackHistoryService(file).Entries[2].Id);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task LegacyHistoryReceivesIdsOnItsNextWrite()
    {
        var file = TempFile();
        try
        {
            await File.WriteAllTextAsync(file,
                "{\"Version\":1,\"Entries\":[{\"Title\":\"Legacy\",\"StationName\":\"Radio One\",\"HeardAt\":\"2026-01-01T00:00:00+00:00\"}]}");
            var history = new TrackHistoryService(file);
            Assert.AreNotEqual(Guid.Empty, history.Entries[0].Id);
            await history.RecordAsync(Station("one"), new RadioTrackInfo("New", "Artist"));
            var reloaded = new TrackHistoryService(file);
            Assert.AreEqual(history.Entries[1].Id, reloaded.Entries[1].Id);
            StringAssert.Contains(await File.ReadAllTextAsync(file), "\"Version\":2", StringComparison.Ordinal);
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
