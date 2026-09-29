using smodr.Models;

namespace smodr.Services;

public sealed class RadioDirectoryService : IDisposable
{
    private const string DirectoryBaseUrl = "https://de1.api.radio-browser.info/json";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public RadioDirectoryService(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
    }

    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        GetStationsAsync(
            $"{DirectoryBaseUrl}/stations/topvote/{Math.Clamp(limit, 1, 100)}?hidebroken=true",
            cancellationToken);

    public Task<IReadOnlyList<RadioStation>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetPopularStationsAsync(limit, cancellationToken);
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        return GetStationsAsync(
            $"{DirectoryBaseUrl}/stations/search?name={encodedQuery}&limit={Math.Clamp(limit, 1, 100)}&order=votes&reverse=true&hidebroken=true",
            cancellationToken);
    }

    public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(
        string genre,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return GetPopularStationsAsync(limit, cancellationToken);
        }

        var encodedGenre = Uri.EscapeDataString(genre.Trim());
        return GetStationsAsync(
            $"{DirectoryBaseUrl}/stations/bytag/{encodedGenre}?limit={Math.Clamp(limit, 1, 100)}&order=votes&reverse=true&hidebroken=true",
            cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<IReadOnlyList<RadioStation>> GetStationsAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        var stations = await _httpClient.GetFromJsonAsync<List<RadioStation>>(requestUri, cancellationToken)
            ?? [];

        return stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Name)
                && Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
                && streamUri.Scheme is "http" or "https")
            .GroupBy(
                station => string.IsNullOrWhiteSpace(station.Id) ? station.StreamUrl : station.Id,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }
}
