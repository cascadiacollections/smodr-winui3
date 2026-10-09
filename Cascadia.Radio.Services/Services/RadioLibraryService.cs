using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioLibraryService : IRadioLibraryService
{
    private const int CurrentSchemaVersion = 1;
    private const int RecentLimit = 20;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly Lock _writeGate = new();
    private RadioLibraryData _data;
    private bool _readOnly;
    private Task _writeTail = Task.CompletedTask;

    public RadioLibraryService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _data = Load();
    }

    public IReadOnlyList<RadioStation> Favorites => [.. Volatile.Read(ref _data).Favorites];
    public IReadOnlyList<RadioStation> Recents => [.. Volatile.Read(ref _data).Recents];

    public bool IsFavorite(RadioStation station)
    {
        return Volatile.Read(ref _data).Favorites.Any(item => RadioStationIdentity.Matches(item, station));
    }

    public Task ToggleFavoriteAsync(RadioStation station)
    {
        return MutateAsync(data =>
        {
            var index = data.Favorites.FindIndex(item => RadioStationIdentity.Matches(item, station));
            if (index >= 0)
            {
                data.Favorites.RemoveAt(index);
            }
            else
            {
                data.Favorites.Add(station);
            }
        });
    }

    public Task<int> RemoveFavoriteAsync(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        return MutateAsync(data =>
        {
            var index = data.Favorites.FindIndex(item => RadioStationIdentity.Matches(item, station));
            if (index >= 0)
            {
                data.Favorites.RemoveAt(index);
            }

            return index;
        });
    }

    public Task RestoreFavoriteAsync(RadioStation station, int index)
    {
        ArgumentNullException.ThrowIfNull(station);
        return MutateAsync(data =>
        {
            if (data.Favorites.Exists(item => RadioStationIdentity.Matches(item, station)))
            {
                return;
            }

            data.Favorites.Insert(Math.Clamp(index, 0, data.Favorites.Count), station);
        });
    }

    public Task ReorderFavoritesAsync(IReadOnlyList<RadioStation> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        RadioStation[] requested = [.. order];
        return MutateAsync(data =>
        {
            // Match against the saved list rather than trusting the UI snapshot, so a
            // favorite added or removed while a drag was in flight is neither lost nor resurrected.
            var remaining = new List<RadioStation>(data.Favorites);
            var reordered = new List<RadioStation>(remaining.Count);
            foreach (var station in requested)
            {
                var index = remaining.FindIndex(item => RadioStationIdentity.Matches(item, station));
                if (index < 0)
                {
                    continue;
                }

                reordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            reordered.AddRange(remaining);
            data.Favorites = reordered;
        });
    }

    public Task LogRecentAsync(RadioStation station)
    {
        return MutateAsync(data =>
        {
            data.Recents.RemoveAll(item => RadioStationIdentity.Matches(item, station));
            data.Recents.Insert(0, station);
            if (data.Recents.Count > RecentLimit)
            {
                data.Recents.RemoveRange(RecentLimit, data.Recents.Count - RecentLimit);
            }
        });
    }

    public Task ClearRecentsAsync()
    {
        return MutateAsync(data => data.Recents.Clear());
    }

    public Task FlushAsync()
    {
        lock (_writeGate)
        {
            return _writeTail;
        }
    }

    private Task<bool> MutateAsync(Action<RadioLibraryData> mutation)
    {
        return MutateAsync(data =>
        {
            mutation(data);
            return true;
        });
    }

    private Task<T> MutateAsync<T>(Func<RadioLibraryData, T> mutation)
    {
        lock (_writeGate)
        {
            var operation = ApplyMutationAfterAsync(_writeTail, mutation);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    private async Task<T> ApplyMutationAfterAsync<T>(Task previous, Func<RadioLibraryData, T> mutation)
    {
        await previous.ConfigureAwait(false);
        return await Task.Run(() =>
        {
            if (_readOnly)
            {
                throw new IOException("A newer library format cannot be changed by this version of Shoutkit.");
            }

            var current = Volatile.Read(ref _data);
            var next = new RadioLibraryData { Favorites = [.. current.Favorites], Recents = [.. current.Recents] };
            var result = mutation(next);
            Save(next);
            Volatile.Write(ref _data, next);
            return result;
        }).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
            // The caller receives this failure; later queued writes must still run.
        }
    }

    private RadioLibraryData Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var data = JsonSerializer.Deserialize<RadioLibraryData>(File.ReadAllText(_filePath)) ??
                           new RadioLibraryData();
                data.Favorites ??= [];
                data.Recents ??= [];
                _readOnly = data.SchemaVersion > CurrentSchemaVersion;
                return data;
            }
        }
        catch (JsonException exception)
        {
            _writeTail = Task.Run(() =>
            {
                AppDiagnostics.Record("library.invalid-json", exception);
                PreserveInvalidLibrary();
            });
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new RadioLibraryData();
    }

    private void PreserveInvalidLibrary()
    {
        try
        {
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
            File.Copy(_filePath, backup);
        }
        catch (Exception)
        {
            // Opening the app is more important than backing up a damaged library.
        }
    }

    private void Save(RadioLibraryData data)
    {
        AtomicFileWriter.WriteAllText(_filePath, JsonSerializer.Serialize(data, _jsonOptions));
    }

    private sealed class RadioLibraryData
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<RadioStation> Favorites { get; set; } = [];
        public List<RadioStation> Recents { get; set; } = [];
    }
}
