using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioDirectoryService : IRadioDirectoryService
{
    private const string UserAgent = "ShoutkitWindows/0.1 (+https://github.com/cascadiacollections/smodr-winui3)";
    private static readonly Uri[] _defaultServers =
    [
        new("https://all.api.radio-browser.info/"),
        new("https://de1.api.radio-browser.info/"),
        new("https://nl1.api.radio-browser.info/")
    ];

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<Uri> _servers;

    public RadioDirectoryService(HttpClient httpClient, IReadOnlyList<Uri>? servers = null)
    {
        _httpClient = httpClient;
        _servers = servers ?? _defaultServers;
        if (_servers.Count == 0 || _servers.Any(uri => uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("At least one HTTPS directory server is required.", nameof(servers));
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }
    }

    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        GetStationsAsync($"json/stations/topvote/{Math.Clamp(limit, 1, 100)}?hidebroken=true", cancellationToken);

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
            $"json/stations/search?name={encodedQuery}&limit={Math.Clamp(limit, 1, 100)}&order=votes&reverse=true&hidebroken=true",
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
            $"json/stations/bytag/{encodedGenre}?limit={Math.Clamp(limit, 1, 100)}&order=votes&reverse=true&hidebroken=true",
            cancellationToken);
    }

    private async Task<IReadOnlyList<RadioStation>> GetStationsAsync(string path, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var server in _servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requestUri = new Uri(server, path);
                var stations = await _httpClient.GetFromJsonAsync<List<RadioStation>>(requestUri, cancellationToken) ?? [];
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
        {
            AppDiagnostics.Record("directory.request", lastError);
        }

        throw new RadioDirectoryUnavailableException("Radio directory unavailable. Check your connection and try again.", lastError);
    }
}

public sealed class RadioDirectoryUnavailableException(string message, Exception? innerException)
    : Exception(message, innerException);
