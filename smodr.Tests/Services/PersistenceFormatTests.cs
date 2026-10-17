using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using smodr.Models;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

/// <summary>
/// On-disk compatibility of the portable services' JSON. "-current" fixtures were written by the
/// reflection-based serializer that shipped before source generation; rewriting unchanged data must
/// reproduce them exactly. Legacy and future-version fixtures cover the variants Load already accepts.
/// </summary>
[TestClass]
public sealed class PersistenceFormatTests
{
    private const string CafeName = "Café <Ñ> & \"Jazz\" — ☕";
    private static readonly string[] _legacyTitles = ["Newer", "Older"];
    private static readonly JsonSerializerOptions _shippedExportOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _shippedCounterOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly RadioStation _scratchStation = new()
    {
        Id = "scratch", Name = "Scratch", StreamUrl = "https://example.com/scratch.mp3"
    };

    [TestMethod]
    public async Task LibraryCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("library-current.json");

        var library = new RadioLibraryService(path);
        Assert.HasCount(2, library.Favorites);
        var cafe = library.Favorites[0];
        Assert.AreEqual("9617a958-0601-11e8-ae97-52543be04c81", cafe.Id);
        Assert.AreEqual(CafeName, cafe.Name);
        Assert.AreEqual("https://stream.example.com/jazz.mp3?x=1&y=2", cafe.StreamUrl);
        Assert.AreEqual(128, cafe.Bitrate);
        Assert.AreEqual("France", cafe.Country);
        CollectionAssert.AreEqual(new[] { "Plain", CafeName }, library.Recents.Select(item => item.Name).ToArray());

        await library.ToggleFavoriteAsync(_scratchStation);
        await library.ToggleFavoriteAsync(_scratchStation);

        Assert.AreEqual(PersistenceFixture.Read("library-current.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task LibraryLegacyUnversionedFileLoadsWithDefaultsAndStaysWritable()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("library-legacy-unversioned.json");

        var library = new RadioLibraryService(path);
        var station = library.Favorites.Single();
        Assert.AreEqual("legacy-1", station.Id);
        Assert.AreEqual(64, station.Bitrate);
        // Members absent from old files keep their initializers rather than becoming null.
        Assert.AreEqual(string.Empty, station.ArtworkUrl);
        Assert.AreEqual(string.Empty, station.Tags);
        Assert.IsEmpty(library.Recents);

        await library.LogRecentAsync(station);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(1, saved.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.AreEqual("legacy-1",
            saved.RootElement.GetProperty("Recents")[0].GetProperty("stationuuid").GetString());
    }

    [TestMethod]
    public async Task LibraryFromNewerVersionLoadsReadOnly()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("library-future-v2.json");

        var library = new RadioLibraryService(path);
        Assert.AreEqual("future-1", library.Favorites.Single().Id);
        await Assert.ThrowsExactlyAsync<IOException>(() => library.ToggleFavoriteAsync(_scratchStation));
        Assert.AreEqual(PersistenceFixture.Read("library-future-v2.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task TrackHistoryCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("track-history-current.json");

        var history = new TrackHistoryService(path);
        Assert.HasCount(2, history.Entries);
        var latest = history.Entries[0];
        Assert.AreEqual(Guid.Parse("73d2693d-ad5f-48d4-880c-35704c650b35"), latest.Id);
        Assert.AreEqual("Untitled", latest.Title);
        Assert.IsNull(latest.Artist);
        var older = history.Entries[1];
        Assert.AreEqual("Stan Getz & João Gilberto", older.Artist);
        Assert.AreEqual(TimeSpan.FromHours(2), older.HeardAt.Offset);
        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 1, 8, 30, 15, 123, TimeSpan.FromHours(2)).AddTicks(4567), older.HeardAt);
        Assert.AreEqual("https://music.example.com/album/1?i=2", older.AppleMusicUrl);

        // Hearing the latest track again (with an older cue time) rewrites the same data.
        var plain = new RadioStation { Name = "Plain", StreamUrl = "http://example.com/plain" };
        await history.RecordAtAsync(plain, new RadioTrackInfo("Untitled", null), older.HeardAt);

        Assert.AreEqual(PersistenceFixture.Read("track-history-current.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task TrackHistoryV1FileIsNormalizedAndStaysWritable()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("track-history-legacy-v1.json");

        var history = new TrackHistoryService(path);
        CollectionAssert.AreEqual(_legacyTitles, history.Entries.Select(item => item.Title).ToArray());
        Assert.IsTrue(history.Entries.All(item => item.Id != Guid.Empty));
        Assert.AreEqual(string.Empty, history.Entries[0].ArtworkUrl);
        Assert.AreEqual(string.Empty, history.Entries[0].StationArtworkUrl);
        Assert.AreEqual(TimeSpan.FromHours(1), history.Entries[0].HeardAt.Offset);

        await history.ClearAsync();

        Assert.AreEqual("""{"Version":2,"Entries":[]}""", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task TrackHistoryFromNewerVersionLoadsReadOnly()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("track-history-future-v3.json");

        var history = new TrackHistoryService(path);
        Assert.AreEqual("From the future", history.Entries.Single().Title);
        await Assert.ThrowsExactlyAsync<IOException>(history.ClearAsync);
        Assert.AreEqual(PersistenceFixture.Read("track-history-future-v3.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task DirectorySnapshotCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("directory-snapshot-current.json");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var key = RadioDirectorySnapshotCache.PopularKey(10);

        var cache = new RadioDirectorySnapshotCache(path, clock);
        var stations = await cache.GetAsync(key, TimeSpan.FromHours(1));
        Assert.IsNotNull(stations);
        CollectionAssert.AreEqual(new[] { CafeName, "Plain" }, stations.Select(item => item.Name).ToArray());

        await cache.StoreAsync(key, stations);

        Assert.AreEqual(PersistenceFixture.Read("directory-snapshot-current.json"),
            await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task DirectorySnapshotFromAnotherVersionIsIgnored()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("directory-snapshot-future-v2.json");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));

        var cache = new RadioDirectorySnapshotCache(path, clock);

        Assert.IsNull(await cache.GetAsync(RadioDirectorySnapshotCache.PopularKey(10), TimeSpan.FromHours(1)));
    }

    [TestMethod]
    public async Task HistoryExportMatchesShippedShape()
    {
        using var fixture = new PersistenceFixture();
        var history = new TrackHistoryService(fixture.Install("track-history-current.json"));

        var export = await LocalDataExport.HistoryAsync(history);

        using var document = JsonDocument.Parse(export);
        var exportedAt = document.RootElement.GetProperty("exportedAtUtc").GetDateTimeOffset();
        Assert.AreEqual(
            ShippedSerialize(new { schemaVersion = 1, exportedAtUtc = exportedAt, entries = history.Entries }),
            export);
    }

    [TestMethod]
    public void DiagnosticsExportMatchesShippedShape()
    {
        var counters = new RuntimeDiagnosticCounters();
        counters.Increment(RuntimeCounter.AlbumMatch);
        counters.Increment(RuntimeCounter.MetadataAccepted);

        var export = LocalDataExport.Diagnostics(counters);

        using var document = JsonDocument.Parse(export);
        var capturedAt = document.RootElement.GetProperty("capturedAtUtc").GetDateTimeOffset();
        Assert.AreEqual(
            ShippedSerialize(new { schemaVersion = 1, capturedAtUtc = capturedAt, counters = counters.Snapshot() }),
            export);
    }

    [TestMethod]
    public async Task RuntimeCounterFileMatchesShippedShape()
    {
        var directory = Directory.CreateTempSubdirectory("shoutkit-counters-format");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime-counters.json");
            var counters = new RuntimeDiagnosticCounters();
            counters.Increment(RuntimeCounter.RecoveryExhausted);

            await counters.SaveAsync(path);

            var saved = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(saved);
            var capturedAt = document.RootElement.GetProperty("capturedAtUtc").GetDateTimeOffset();
            var shipped = JsonSerializer.Serialize(
                new { SchemaVersion = 1, CapturedAtUtc = capturedAt, Counters = counters.Snapshot() },
                _shippedCounterOptions);
            Assert.AreEqual(shipped, saved);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    // The reflection configuration the exports used before source generation.
    private static string ShippedSerialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _shippedExportOptions);
    }
}
