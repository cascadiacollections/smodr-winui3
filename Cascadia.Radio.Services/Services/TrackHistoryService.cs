using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

/// <summary>Ordered, bounded local heard-track history, separate from the station library.</summary>
public sealed class TrackHistoryService : ITrackHistoryService
{
    private const int SchemaVersion = 2;
    private const long MaxFileBytes = 2_000_000;
    private readonly string _filePath;
    private readonly int _limit;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private Task _writeTail = Task.CompletedTask;
    private HistoryData _data;
    private bool _readOnly;

    public TrackHistoryService(string filePath, int limit = 1000, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        _filePath = Path.GetFullPath(filePath);
        _limit = limit;
        _clock = clock ?? TimeProvider.System;
        _data = Load();
    }

    public IReadOnlyList<HeardTrack> Entries => [.. Volatile.Read(ref _data).Entries];

    public Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(track);
        lock (_gate)
        {
            var operation = RecordAfterAsync(_writeTail, station, track);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork)
    {
        if (entryId == Guid.Empty) throw new ArgumentException("An entry ID is required.", nameof(entryId));
        ArgumentNullException.ThrowIfNull(artwork);
        lock (_gate)
        {
            var operation = UpdateArtworkAfterAsync(_writeTail, entryId, artwork);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task ClearAsync()
    {
        lock (_gate)
        {
            var operation = ClearAfterAsync(_writeTail);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _writeTail;
    }

    private async Task ClearAfterAsync(Task previous)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (_readOnly) throw new IOException("Unreadable or newer track history cannot be changed by this app.");
            var next = new HistoryData();
            Save(next);
            Volatile.Write(ref _data, next);
        }).ConfigureAwait(false);
    }

    private async Task<Guid> RecordAfterAsync(Task previous, RadioStation station, RadioTrackInfo track)
    {
        await previous.ConfigureAwait(false);
        return await Task.Run(() =>
        {
            if (_readOnly) throw new IOException("Unreadable or newer track history cannot be changed by this app.");
            var current = Volatile.Read(ref _data);
            var next = new HistoryData { Entries = [.. current.Entries] };
            var timestamp = _clock.GetUtcNow();
            var latest = next.Entries.FirstOrDefault();
            Guid entryId;
            if (latest is not null
                && (string.IsNullOrWhiteSpace(station.Id)
                    ? string.IsNullOrWhiteSpace(latest.StationId)
                        && string.Equals(latest.StationName, station.Name, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(latest.StationId, station.Id, StringComparison.OrdinalIgnoreCase))
                && string.Equals(latest.Title, track.Title, StringComparison.Ordinal)
                && string.Equals(latest.Artist, track.Artist, StringComparison.Ordinal))
            {
                entryId = latest.Id == Guid.Empty ? Guid.NewGuid() : latest.Id;
                next.Entries[0] = new HeardTrack
                {
                    Id = entryId,
                    StationId = latest.StationId,
                    StationName = station.Name,
                    Title = latest.Title,
                    Artist = latest.Artist,
                    HeardAt = timestamp > latest.HeardAt ? timestamp : latest.HeardAt,
                    ArtworkUrl = latest.ArtworkUrl,
                    StationArtworkUrl = station.ArtworkUrl,
                    AppleMusicUrl = latest.AppleMusicUrl
                };
            }
            else
            {
                entryId = Guid.NewGuid();
                next.Entries.Insert(0, new HeardTrack
                {
                    Id = entryId,
                    StationId = station.Id,
                    StationName = station.Name,
                    Title = track.Title,
                    Artist = track.Artist,
                    HeardAt = timestamp,
                    StationArtworkUrl = station.ArtworkUrl
                });
            }

            if (next.Entries.Count > _limit) next.Entries.RemoveRange(_limit, next.Entries.Count - _limit);
            Save(next);
            Volatile.Write(ref _data, next);
            return entryId;
        }).ConfigureAwait(false);
    }

    private async Task UpdateArtworkAfterAsync(Task previous, Guid entryId,
        AlbumArtworkMatch artwork)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (_readOnly) throw new IOException("Unreadable or newer track history cannot be changed by this app.");
            var current = Volatile.Read(ref _data);
            var next = new HistoryData { Entries = [.. current.Entries] };
            var index = next.Entries.FindIndex(item => item.Id == entryId);
            if (index < 0) return;
            var previousEntry = next.Entries[index];
            next.Entries[index] = new HeardTrack
            {
                Id = previousEntry.Id,
                StationId = previousEntry.StationId,
                StationName = previousEntry.StationName,
                Title = previousEntry.Title,
                Artist = previousEntry.Artist,
                HeardAt = previousEntry.HeardAt,
                StationArtworkUrl = previousEntry.StationArtworkUrl,
                ArtworkUrl = artwork.ArtworkUrl.AbsoluteUri,
                AppleMusicUrl = artwork.StoreUrl?.AbsoluteUri ?? string.Empty
            };
            Save(next);
            Volatile.Write(ref _data, next);
        }).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* A failed write must not poison later updates. */ }
    }

    private HistoryData Load()
    {
        try
        {
            var file = new FileInfo(_filePath);
            if (!file.Exists) return new HistoryData();
            if (file.Length > MaxFileBytes) throw new IOException("Track history exceeds its size limit.");
            var data = JsonSerializer.Deserialize<HistoryData>(File.ReadAllText(_filePath)) ?? new HistoryData();
            data.Entries ??= [];
            _readOnly = data.Version > SchemaVersion;
            data.Entries = [.. data.Entries.Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .OrderByDescending(item => item.HeardAt).Take(_limit).Select(EnsureId)];
            return data;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preserve an unreadable or future-looking file for manual recovery.
            _readOnly = true;
            AppDiagnostics.Record("track-history.read", exception);
            return new HistoryData();
        }
    }

    private void Save(HistoryData data)
    {
        AtomicFileWriter.WriteAllText(_filePath, JsonSerializer.Serialize(data));
    }

    private static HeardTrack EnsureId(HeardTrack entry) => entry.Id != Guid.Empty ? entry : new HeardTrack
    {
        Id = Guid.NewGuid(),
        StationId = entry.StationId,
        StationName = entry.StationName,
        Title = entry.Title,
        Artist = entry.Artist,
        HeardAt = entry.HeardAt,
        ArtworkUrl = entry.ArtworkUrl,
        StationArtworkUrl = entry.StationArtworkUrl,
        AppleMusicUrl = entry.AppleMusicUrl
    };

    private sealed class HistoryData
    {
        public int Version { get; set; } = SchemaVersion;
        public List<HeardTrack> Entries { get; set; } = [];
    }
}
