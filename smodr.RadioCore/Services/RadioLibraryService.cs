using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioLibraryService : IRadioLibraryService
{
    private const int CurrentSchemaVersion = 1;
    private const int RecentLimit = 20;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private RadioLibraryData _data;
    private bool _readOnly;

    public RadioLibraryService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections",
            "ShoutkitWindows",
            "library.json");
        _data = Load();
    }

    public IReadOnlyList<RadioStation> Favorites => _data.Favorites;
    public IReadOnlyList<RadioStation> Recents => _data.Recents;

    public bool IsFavorite(RadioStation station) =>
        _data.Favorites.Any(item => RadioStationIdentity.Matches(item, station));

    public void ToggleFavorite(RadioStation station)
    {
        Mutate(() =>
        {
            var index = _data.Favorites.FindIndex(item => RadioStationIdentity.Matches(item, station));
            if (index >= 0)
            {
                _data.Favorites.RemoveAt(index);
            }
            else
            {
                _data.Favorites.Add(station);
            }
        });
    }

    public void LogRecent(RadioStation station)
    {
        Mutate(() =>
        {
            _data.Recents.RemoveAll(item => RadioStationIdentity.Matches(item, station));
            _data.Recents.Insert(0, station);
            if (_data.Recents.Count > RecentLimit)
            {
                _data.Recents.RemoveRange(RecentLimit, _data.Recents.Count - RecentLimit);
            }
        });
    }

    private void Mutate(Action mutation)
    {
        if (_readOnly)
        {
            throw new IOException("A newer library format cannot be changed by this version of Shoutkit.");
        }

        var previous = new RadioLibraryData
        {
            Favorites = [.. _data.Favorites],
            Recents = [.. _data.Recents]
        };
        mutation();
        try
        {
            Save();
        }
        catch
        {
            _data = previous;
            throw;
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
            AppDiagnostics.Record("library.invalid-json", exception);
            PreserveInvalidLibrary();
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

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_data, _jsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed class RadioLibraryData
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<RadioStation> Favorites { get; set; } = [];
        public List<RadioStation> Recents { get; set; } = [];
    }
}
