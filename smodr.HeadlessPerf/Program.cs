using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using smodr.Models;
using smodr.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// No WinUI application, audio engine, user profile, HTTP client, or network access.
var directory = Directory.CreateTempSubdirectory("shoutkit-headless-perf-");
try
{
    var libraryPath = Path.Combine(directory.FullName, "library.json");
    var historyPath = Path.Combine(directory.FullName, "history.json");
    var cachePath = Path.Combine(directory.FullName, "directory.json");
    var stations = Enumerable.Range(0, 60).Select(index => new RadioStation
    {
        Id = $"synthetic-{index}",
        Name = $"Synthetic station {index}",
        StreamUrl = $"https://example.com/{index}"
    }).ToArray();
    var library = new RadioLibraryService(libraryPath);
    var history = new TrackHistoryService(historyPath);
    var cache = new RadioDirectorySnapshotCache(cachePath);
    foreach (var station in stations.Take(20))
    {
        await library.ToggleFavoriteAsync(station);
        await library.LogRecentAsync(station);
        await history.RecordAsync(station, new RadioTrackInfo("Synthetic song", "Synthetic artist"));
    }
    var key = RadioDirectorySnapshotCache.PopularKey(60);
    await cache.StoreAsync(key, stations);
    var bitmap = CreateBitmap();
    var notices = Path.Combine(AppContext.BaseDirectory, "SoftwareLicenses.txt");
    var warmText = await File.ReadAllTextAsync(notices);
    var artworkCache = new ArtworkMemoryCache<byte[]>(48, 8 * 1024 * 1024);
    artworkCache.Put("synthetic", bitmap, bitmap.Length);
    Measurement[] results = [
    await MeasureAsync("startup.profile-and-directory-preparation", async () =>
    {
        var loadedLibrary = new RadioLibraryService(libraryPath);
        var loadedHistory = new TrackHistoryService(historyPath);
        var loaded = await new RadioDirectorySnapshotCache(cachePath).GetAsync(key, TimeSpan.FromDays(30));
        Require(loadedLibrary.Favorites.Count == 20 && loadedHistory.Entries.Count == 20 && loaded?.Count == 60);
    }),
    await MeasureAsync("directory.warm-60-station-copy", async () =>
        Require((await cache.GetAsync(key, TimeSpan.FromDays(30)))?.Count == 60)),
    await MeasureAsync("artwork.native-bmp-decode-512-to-128", async () =>
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bitmap);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var size = ArtworkSizing.DecodeDimensions(decoder.PixelWidth, decoder.PixelHeight, 128)
            ?? throw new InvalidOperationException("Synthetic dimensions rejected.");
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform { ScaledWidth = (uint)size.Width, ScaledHeight = (uint)size.Height },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        Require(pixels.DetachPixelData().Length == 128 * 128 * 4);
    }),
    await MeasureAsync("artwork.encoded-cache-hit", () =>
    {
        Require(artworkCache.TryGet("synthetic", out var bytes) && ReferenceEquals(bytes, bitmap));
        return Task.CompletedTask;
    }),
    await MeasureAsync("licenses.read-and-split", async () =>
        Require(SoftwareLicenseText.Split(await File.ReadAllTextAsync(notices)).Count > 0)),
    await MeasureAsync("licenses.split-only", () =>
    {
        var sections = SoftwareLicenseText.Split(warmText);
        Require(sections.Sum(section => section.Length) == warmText.Length);
        return Task.CompletedTask;
    })];
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        capturedAtUtc = DateTimeOffset.UtcNow,
        runtime = RuntimeInformation.FrameworkDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        hostArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        processorCount = Environment.ProcessorCount,
        os = RuntimeInformation.OSDescription,
        stationCount = stations.Length,
        noticeCharacters = warmText.Length,
        sourceBitmapBytes = bitmap.Length,
        limitations = "Synthetic fixtures; OS file cache not cleared. Excludes WinUI startup/layout, BitmapImage rendering, audio, network, and native memory. Managed allocations include all process threads.",
        measurements = results
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { directory.Delete(recursive: true); }

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("Performance fixture correctness check failed.");
}

static async Task<Measurement> MeasureAsync(string name, Func<Task> action)
{
    for (var warmup = 0; warmup < 3; warmup++) await action();
    var samples = new double[25];
    var allocated = GC.GetTotalAllocatedBytes(precise: true);
    for (var index = 0; index < samples.Length; index++)
    {
        var start = Stopwatch.GetTimestamp();
        await action();
        samples[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
    Array.Sort(samples);
    return new Measurement(name, samples.Length, samples[0], samples[samples.Length / 2],
        samples[(int)Math.Ceiling(samples.Length * 0.95) - 1], allocated / samples.Length);
}

static byte[] CreateBitmap()
{
    const int edge = 512;
    const int pixelBytes = edge * edge * 3;
    using var buffer = new MemoryStream();
    using var writer = new BinaryWriter(buffer);
    writer.Write((ushort)0x4d42);
    writer.Write(54 + pixelBytes);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(edge);
    writer.Write(edge);
    writer.Write((ushort)1);
    writer.Write((ushort)24);
    writer.Write(0);
    writer.Write(pixelBytes);
    writer.Write(new byte[16]);
    writer.Write(new byte[pixelBytes]);
    return buffer.ToArray();
}

internal sealed record Measurement(string Name, int Samples, double MinMs, double MedianMs, double P95Ms, long ManagedBytesPerOperation);
