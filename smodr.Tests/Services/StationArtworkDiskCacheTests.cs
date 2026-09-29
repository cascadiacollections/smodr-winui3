using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class StationArtworkDiskCacheTests
{
    [TestMethod]
    public async Task ArtworkSurvivesCacheRestartAndExpires()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-artwork-test-{Guid.NewGuid():N}");
        var uri = new Uri("https://example.com/station.png");
        var bytes = new byte[] { 137, 80, 78, 71 };
        try
        {
            var cache = new StationArtworkDiskCache(directory);
            await cache.StoreAsync(uri, bytes);
            var restarted = new StationArtworkDiskCache(directory);
            CollectionAssert.AreEqual(bytes, await restarted.TryReadAsync(uri));

            var file = Directory.GetFiles(directory, "*.img").Single();
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-31));
            Assert.IsNull(await restarted.TryReadAsync(uri));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task OversizeArtworkIsNotPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-artwork-test-{Guid.NewGuid():N}");
        var uri = new Uri("https://example.com/large.png");
        try
        {
            var cache = new StationArtworkDiskCache(directory);
            await cache.StoreAsync(uri, new byte[1_000_001]);
            Assert.IsNull(await cache.TryReadAsync(uri));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
