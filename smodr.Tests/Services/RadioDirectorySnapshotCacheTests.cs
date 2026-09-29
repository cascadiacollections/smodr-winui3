using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioDirectorySnapshotCacheTests
{
    [TestMethod]
    public async Task SnapshotSurvivesRestartAndExpires()
    {
        var path = NewPath();
        var clock = new TestClock();
        try
        {
            var cache = new RadioDirectorySnapshotCache(path, clock);
            await cache.StoreAsync(RadioDirectorySnapshotCache.PopularKey(60), (RadioStation[])[Station("one")]);

            var restarted = new RadioDirectorySnapshotCache(path, clock);
            var stations = await restarted.GetAsync(RadioDirectorySnapshotCache.PopularKey(60), TimeSpan.FromDays(30));
            Assert.HasCount(1, stations!);
            Assert.AreEqual("one", stations![0].Id);

            clock.Advance(TimeSpan.FromDays(31));
            Assert.IsNull(await restarted.GetAsync(RadioDirectorySnapshotCache.PopularKey(60), TimeSpan.FromDays(30)));
        }
        finally { DeleteTestDirectory(path); }
    }

    [TestMethod]
    public async Task ConcurrentStoresRemainReadableAndBounded()
    {
        var path = NewPath();
        try
        {
            var cache = new RadioDirectorySnapshotCache(path);
            await Task.WhenAll(Enumerable.Range(0, 30)
                .Select(index => cache.StoreAsync($"genre:{index}",
                    (RadioStation[])[Station(index.ToString(System.Globalization.CultureInfo.InvariantCulture))])));
            await cache.FlushAsync();

            var restarted = new RadioDirectorySnapshotCache(path);
            var found = 0;
            for (var index = 0; index < 30; index++)
            {
                if (await restarted.GetAsync($"genre:{index}", TimeSpan.FromDays(1)) is not null) found++;
            }

            Assert.AreEqual(24, found);
        }
        finally { DeleteTestDirectory(path); }
    }

    [TestMethod]
    public async Task CorruptSnapshotIsIgnoredAndReplaced()
    {
        var path = NewPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{bad-json");
            var cache = new RadioDirectorySnapshotCache(path);
            Assert.IsNull(await cache.GetAsync("popular:60", TimeSpan.FromDays(1)));

            await cache.StoreAsync("popular:60", (RadioStation[])[Station("one")]);
            var restarted = new RadioDirectorySnapshotCache(path);
            Assert.HasCount(1, (await restarted.GetAsync("popular:60", TimeSpan.FromDays(1)))!);
        }
        finally { DeleteTestDirectory(path); }
    }

    [TestMethod]
    public void SearchKeysDoNotExposeQueryText()
    {
        var key = RadioDirectorySnapshotCache.SearchKey(" Jazz Station ");
        Assert.IsFalse(key.Contains("Jazz", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(key, RadioDirectorySnapshotCache.SearchKey("jazz station"));
    }

    private static RadioStation Station(string id) => new()
    {
        Id = id,
        Name = $"Station {id}",
        StreamUrl = $"https://example.com/{id}"
    };

    private static string NewPath() => Path.Combine(
        Path.GetTempPath(), $"shoutkit-directory-cache-{Guid.NewGuid():N}", "cache.json");

    private static void DeleteTestDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
