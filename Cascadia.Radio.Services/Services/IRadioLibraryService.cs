using smodr.Models;

namespace smodr.Services;

public interface IRadioLibraryService
{
    IReadOnlyList<RadioStation> Favorites { get; }
    IReadOnlyList<RadioStation> Recents { get; }
    bool IsFavorite(RadioStation station);
    Task ToggleFavoriteAsync(RadioStation station);

    /// <summary>Removes a favorite and returns its former position, or -1 when it was not saved.</summary>
    Task<int> RemoveFavoriteAsync(RadioStation station);

    /// <summary>Re-adds a removed favorite at its former position (clamped); no-op if already saved.</summary>
    Task RestoreFavoriteAsync(RadioStation station, int index);

    /// <summary>
    ///     Applies a user-chosen order. Saved favorites missing from <paramref name="order" /> keep their relative order
    ///     at the end.
    /// </summary>
    Task ReorderFavoritesAsync(IReadOnlyList<RadioStation> order);

    Task LogRecentAsync(RadioStation station);
    Task ClearRecentsAsync();
    Task FlushAsync();
}
