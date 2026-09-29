using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioLibraryService
{
    private const int RecentLimit = 20;
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private RadioLibraryData _data;

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
        _data.Favorites.Any(item => StationMatches(item, station));

    public void ToggleFavorite(RadioStation station)
    {
        var index = _data.Favorites.FindIndex(item => StationMatches(item, station));
        if (index >= 0)
        {
            _data.Favorites.RemoveAt(index);
        }
        else
        {
            _data.Favorites.Add(station);
        }

        Save();
    }

    public void LogRecent(RadioStation station)
    {
        _data.Recents.RemoveAll(item => StationMatches(item, station));
        _data.Recents.Insert(0, station);
        if (_data.Recents.Count > RecentLimit)
        {
            _data.Recents.RemoveRange(RecentLimit, _data.Recents.Count - RecentLimit);
        }

        Save();
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
                return data;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new();
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

    private static bool StationMatches(RadioStation left, RadioStation right) =>
        !string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id)
            ? string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
            : string.Equals(left.StreamUrl, right.StreamUrl, StringComparison.OrdinalIgnoreCase);

    private sealed class RadioLibraryData
    {
        public List<RadioStation> Favorites { get; set; } = [];
        public List<RadioStation> Recents { get; set; } = [];
    }
}
