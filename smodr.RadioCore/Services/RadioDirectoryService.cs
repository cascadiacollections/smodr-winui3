using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioDirectoryService : IRadioDirectoryService, IStationPlayReporter
{
    private static readonly string _userAgent = $"ShoutkitWindows/{typeof(RadioDirectoryService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} (+https://github.com/cascadiacollections/smodr-winui3)";
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private const int MaxResponseStations = 1_000;
    private const int MaxReturnedStations = 100;
    private static readonly RadioBrowserServers _sharedServers = new();

    private readonly HttpClient _httpClient;
    private readonly HttpClient _reportClient;
    private readonly IReadOnlyList<Uri>? _servers;
    private readonly IRadioBrowserServerProvider _serverProvider;

    public RadioDirectoryService(HttpClient httpClient, IReadOnlyList<Uri>? servers = null,
        HttpClient? reportClient = null, IRadioBrowserServerProvider? serverProvider = null)
    {
        _httpClient = httpClient;
        _reportClient = reportClient ?? httpClient;
        _servers = servers;
        _serverProvider = serverProvider ?? _sharedServers;
        if (_servers is not null && (_servers.Count == 0 || _servers.Any(uri => uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("At least one HTTPS directory server is required.", nameof(servers));
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        }
        if (_reportClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _reportClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        }
    }

    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        GetStationsAsync($"json/stations/topclick/{Math.Clamp(limit, 1, MaxReturnedStations)}?hidebroken=true",
            limit, cancellationToken);

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
            $"json/stations/search?name={encodedQuery}&limit={Math.Clamp(limit, 1, MaxReturnedStations)}&order=clickcount&reverse=true&hidebroken=true",
            limit, cancellationToken);
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
            $"json/stations/bytag/{encodedGenre}?limit={Math.Clamp(limit, 1, MaxReturnedStations)}&order=clickcount&reverse=true&hidebroken=true",
            limit, cancellationToken);
    }

    public async Task ReportPlayAsync(string stationId, CancellationToken cancellationToken = default)
    {
        // Bundled and third-party stations do not have Radio Browser UUIDs.
        if (!Guid.TryParseExact(stationId, "D", out var stationUuid)) return;

        Exception? lastError = null;
        foreach (var server in _servers ?? await _serverProvider.GetServersAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _reportClient.GetAsync(
                    new Uri(server, $"json/url/{stationUuid:D}"),
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var result = await ReadJsonAsync(response.Content, 16 * 1024, cancellationToken).ConfigureAwait(false);
                if (result.RootElement.ValueKind != JsonValueKind.Object || !result.RootElement.TryGetProperty("ok", out var ok)
                    || !(ok.ValueKind == JsonValueKind.True
                        || (ok.ValueKind == JsonValueKind.String && string.Equals(ok.GetString(), "true", StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException("Radio Browser rejected the play report.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidDataException)
            {
                lastError = exception;
            }
        }

        if (lastError is not null) AppDiagnostics.Record("directory.play-report", lastError);
    }

    private async Task<IReadOnlyList<RadioStation>> GetStationsAsync(string path, int limit,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var server in _servers ?? await _serverProvider.GetServersAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _httpClient.GetAsync(new Uri(server, path),
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await ReadStationsAsync(response.Content, Math.Clamp(limit, 1, MaxReturnedStations),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                or IOException or InvalidDataException or OperationCanceledException)
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

    private static async Task<IReadOnlyList<RadioStation>> ReadStationsAsync(
        HttpContent content, int limit, CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(content, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Radio directory response was not an array.");

        var stations = new List<RadioStation>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (++scanned > MaxResponseStations)
                throw new InvalidDataException("Radio directory returned too many stations.");
            var entry = item.Deserialize<RadioStation>();
            // Legacy country names remain readable in saved stations and deep
            // links; directory responses use the standardized country code.
            var station = entry is null ? null : new RadioStation
            {
                Id = entry.Id,
                Name = entry.Name,
                StreamUrl = entry.StreamUrl,
                ArtworkUrl = entry.ArtworkUrl,
                Tags = entry.Tags,
                CountryCode = entry.CountryCode,
                Codec = entry.Codec,
                Bitrate = entry.Bitrate,
            };
            if (station is null || string.IsNullOrWhiteSpace(station.Name)
                || !Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
                || streamUri.Scheme is not ("http" or "https")) continue;
            var identity = string.IsNullOrWhiteSpace(station.Id) ? station.StreamUrl : station.Id;
            if (identities.Add(identity) && stations.Count < limit) stations.Add(station);
        }
        return stations;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        cancellationToken = timeout.Token;
        if (content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("Radio directory response exceeded the size limit.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("Radio directory response exceeded the size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
    }
}

public sealed class RadioDirectoryUnavailableException(string message, Exception? innerException)
    : Exception(message, innerException);
