using smodr.Models;

namespace smodr.Services;

public interface IRadioLibraryService
{
    IReadOnlyList<RadioStation> Favorites { get; }
    IReadOnlyList<RadioStation> Recents { get; }
    bool IsFavorite(RadioStation station);
    Task ToggleFavoriteAsync(RadioStation station);
    Task LogRecentAsync(RadioStation station);
    Task FlushAsync();
}
