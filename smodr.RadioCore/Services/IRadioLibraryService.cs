using smodr.Models;

namespace smodr.Services;

public interface IRadioLibraryService
{
    IReadOnlyList<RadioStation> Favorites { get; }
    IReadOnlyList<RadioStation> Recents { get; }
    bool IsFavorite(RadioStation station);
    void ToggleFavorite(RadioStation station);
    void LogRecent(RadioStation station);
}
