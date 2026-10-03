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
    private Task _writeTail = Task.CompletedTask;
    private RadioLibraryData _data;
    private bool _readOnly;

    public RadioLibraryService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _data = Load();
    }

    public IReadOnlyList<RadioStation> Favorites => [.. Volatile.Read(ref _data).Favorites];
    public IReadOnlyList<RadioStation> Recents => [.. Volatile.Read(ref _data).Recents];

    public bool IsFavorite(RadioStation station) =>
        Volatile.Read(ref _data).Favorites.Any(item => RadioStationIdentity.Matches(item, station));

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

    public Task FlushAsync()
    {
        lock (_writeGate)
        {
            return _writeTail;
        }
    }

    private Task MutateAsync(Action<RadioLibraryData> mutation)
    {
        lock (_writeGate)
        {
            var operation = ApplyMutationAfterAsync(_writeTail, mutation);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    private async Task ApplyMutationAfterAsync(Task previous, Action<RadioLibraryData> mutation)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() =>
        {
            if (_readOnly)
            {
                throw new IOException("A newer library format cannot be changed by this version of Shoutkit.");
            }

            var current = Volatile.Read(ref _data);
            var next = new RadioLibraryData
            {
                Favorites = [.. current.Favorites],
                Recents = [.. current.Recents]
            };
            mutation(next);
            Save(next);
            Volatile.Write(ref _data, next);
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
                var data = JsonSerializer.Deserialize<RadioLibraryData>(File.ReadAllText(_filePath)) ?? new();
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

        return new();
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
