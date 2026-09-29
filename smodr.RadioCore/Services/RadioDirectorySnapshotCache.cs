using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

/// <summary>A bounded, best-effort disk snapshot for instant offline discovery.</summary>
public sealed class RadioDirectorySnapshotCache : IRadioDirectorySnapshotCache
{
    private const int SchemaVersion = 1;
    private const int MaxEntries = 24;
    private const int MaxStations = 60;
    private const long MaxFileBytes = 2_000_000;
    private readonly string _filePath;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Task _loadTask;
    private Dictionary<string, CacheEntry> _entries = [];
    private Task _writeTail = Task.CompletedTask;

    public RadioDirectorySnapshotCache(string? filePath = null, TimeProvider? clock = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections", "ShoutkitWindows", "directory-cache.json");
        _clock = clock ?? TimeProvider.System;
        _loadTask = Task.Run(Load);
    }

    public static string PopularKey(int limit) => $"popular:{Math.Clamp(limit, 1, 100)}";
    public static string GenreKey(string genre) => $"genre:{genre.Trim().ToLowerInvariant()}";

    // Do not put a listener's search text in a filename or cache key.
    public static string SearchKey(string query)
    {
        var normalized = query.Trim().ToLowerInvariant();
        return $"search:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))}";
    }

    public async Task<IReadOnlyList<RadioStation>?> GetAsync(string key, TimeSpan maxAge)
    {
        await _loadTask.ConfigureAwait(false);
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (!_entries.TryGetValue(key, out var entry)
                || entry.SavedAt > now
                || now - entry.SavedAt > maxAge)
            {
                return null;
            }

            return [.. entry.Stations];
        }
    }

    public async Task StoreAsync(string key, IReadOnlyList<RadioStation> stations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(stations);
        await _loadTask.ConfigureAwait(false);
        Task operation;
        lock (_gate)
        {
            _entries[key] = new CacheEntry
            {
                SavedAt = _clock.GetUtcNow(),
                Stations = [.. stations.Where(IsValidStation).Take(MaxStations)]
            };
            _entries = _entries
                .OrderByDescending(item => item.Value.SavedAt)
                .Take(MaxEntries)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var snapshot = new CacheData { Entries = new(_entries, StringComparer.Ordinal) };
            operation = SaveAfterAsync(_writeTail, snapshot);
            _writeTail = ObserveCompletionAsync(operation);
        }

        await operation.ConfigureAwait(false);
    }

    public Task FlushAsync()
    {
        lock (_gate) return _writeTail;
    }

    private void Load()
    {
        try
        {
            var file = new FileInfo(_filePath);
            if (!file.Exists || file.Length > MaxFileBytes) return;
            var data = JsonSerializer.Deserialize<CacheData>(File.ReadAllText(_filePath));
            if (data?.Version != SchemaVersion || data.Entries is null) return;
            var entries = data.Entries
                .Where(item => item.Value is { Stations: not null }
                    && item.Value.SavedAt <= _clock.GetUtcNow())
                .OrderByDescending(item => item.Value.SavedAt)
                .Take(MaxEntries)
                .ToDictionary(
                    item => item.Key,
                    item => new CacheEntry
                    {
                        SavedAt = item.Value.SavedAt,
                        Stations = [.. item.Value.Stations.Where(IsValidStation).Take(MaxStations)]
                    },
                    StringComparer.Ordinal);
            lock (_gate) _entries = entries;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Cache corruption must never block startup or directory requests.
            AppDiagnostics.Record("directory.cache-load", exception);
        }
    }

    private static bool IsValidStation(RadioStation? station) =>
        station is not null && !string.IsNullOrWhiteSpace(station.Name)
        && Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private async Task SaveAfterAsync(Task previous, CacheData snapshot)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() => Save(snapshot)).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* A failed cache write must not poison the next one. */ }
    }

    private void Save(CacheData snapshot)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class CacheData
    {
        public int Version { get; set; } = SchemaVersion;
        public Dictionary<string, CacheEntry> Entries { get; set; } = [];
    }

    private sealed class CacheEntry
    {
        public DateTimeOffset SavedAt { get; set; }
        public List<RadioStation> Stations { get; set; } = [];
    }
}
