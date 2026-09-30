using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

/// <summary>Ordered, bounded local heard-track history, separate from the station library.</summary>
public sealed class TrackHistoryService : ITrackHistoryService
{
    private const int SchemaVersion = 1;
    private const long MaxFileBytes = 2_000_000;
    private readonly string _filePath;
    private readonly int _limit;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private Task _writeTail = Task.CompletedTask;
    private HistoryData _data;
    private bool _readOnly;

    public TrackHistoryService(string? filePath = null, int limit = 1000, TimeProvider? clock = null)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections", "ShoutkitWindows", "track-history.json");
        _limit = limit;
        _clock = clock ?? TimeProvider.System;
        _data = Load();
    }

    public IReadOnlyList<HeardTrack> Entries => [.. Volatile.Read(ref _data).Entries];

    public Task RecordAsync(RadioStation station, RadioTrackInfo track)
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

    public Task FlushAsync()
    {
        lock (_gate) return _writeTail;
    }

    private async Task RecordAfterAsync(Task previous, RadioStation station, RadioTrackInfo track)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (_readOnly) throw new IOException("Unreadable or newer track history cannot be changed by this app.");
            var current = Volatile.Read(ref _data);
            var next = new HistoryData { Entries = [.. current.Entries] };
            var timestamp = _clock.GetUtcNow();
            var latest = next.Entries.FirstOrDefault();
            if (latest is not null
                && (string.IsNullOrWhiteSpace(station.Id)
                    ? string.IsNullOrWhiteSpace(latest.StationId)
                        && string.Equals(latest.StationName, station.Name, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(latest.StationId, station.Id, StringComparison.OrdinalIgnoreCase))
                && string.Equals(latest.Title, track.Title, StringComparison.Ordinal)
                && string.Equals(latest.Artist, track.Artist, StringComparison.Ordinal))
            {
                next.Entries[0] = new HeardTrack
                {
                    StationId = latest.StationId,
                    StationName = station.Name,
                    Title = latest.Title,
                    Artist = latest.Artist,
                    HeardAt = timestamp > latest.HeardAt ? timestamp : latest.HeardAt
                };
            }
            else
            {
                next.Entries.Insert(0, new HeardTrack
                {
                    StationId = station.Id,
                    StationName = station.Name,
                    Title = track.Title,
                    Artist = track.Artist,
                    HeardAt = timestamp
                });
            }

            if (next.Entries.Count > _limit) next.Entries.RemoveRange(_limit, next.Entries.Count - _limit);
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
                .OrderByDescending(item => item.HeardAt).Take(_limit)];
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
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data));
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class HistoryData
    {
        public int Version { get; set; } = SchemaVersion;
        public List<HeardTrack> Entries { get; set; } = [];
    }
}
