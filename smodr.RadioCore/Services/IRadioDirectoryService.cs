using smodr.Models;

namespace smodr.Services;

public interface IRadioDirectoryService
{
    Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Filtered search; an empty query with active filters browses by filter alone. Directories without
    ///     server-side filters search unfiltered and filter the returned page locally.
    /// </summary>
    async Task<IReadOnlyList<RadioStation>> SearchAsync(string query, RadioSearchFilters filters, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var stations = await SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        return filters.IsActive ? [.. stations.Where(filters.Matches)] : stations;
    }
}
