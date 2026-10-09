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
}
