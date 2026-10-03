using Cascadia.RadioBrowser;
using smodr.Models;

namespace smodr.Services;

public sealed class RadioDirectoryService : IRadioDirectoryService, IStationPlayReporter
{
    private static readonly string _userAgent = $"ShoutkitWindows/{typeof(RadioDirectoryService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"} (+https://github.com/cascadiacollections/smodr-winui3)";
    private static readonly RadioBrowserServers _sharedServers = new();
    private readonly RadioBrowserClient _client;
    private readonly HttpClient _reportClient;
    private readonly IMirrorProvider _mirrors;

    public RadioDirectoryService(HttpClient httpClient, IReadOnlyList<Uri>? servers = null,
        HttpClient? reportClient = null, IRadioBrowserServerProvider? serverProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _reportClient = reportClient ?? httpClient;
        _mirrors = servers is null ? new ProviderAdapter(serverProvider ?? _sharedServers) : new FixedMirrors(servers);
        if (httpClient.DefaultRequestHeaders.UserAgent.Count == 0) httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        if (_reportClient.DefaultRequestHeaders.UserAgent.Count == 0) _reportClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        _client = new RadioBrowserClient(httpClient, _mirrors, new ClientOptions { UserAgent = _userAgent }, _reportClient);
    }

    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        AdaptAsync(() => _client.GetRankedAsync(limit: Math.Clamp(limit, 1, 100), cancellationToken: cancellationToken));

    public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(query) ? GetPopularStationsAsync(limit, cancellationToken)
            : AdaptAsync(() => _client.SearchAsync(new SearchOptions { Name = query, Limit = Math.Clamp(limit, 1, 100) }, cancellationToken));

    public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(genre) ? GetPopularStationsAsync(limit, cancellationToken)
            : AdaptAsync(() => _client.GetByTagAsync(genre, Math.Clamp(limit, 1, 100), cancellationToken: cancellationToken));

    public async Task ReportPlayAsync(string stationId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(stationId, "D", out var uuid) || uuid == Guid.Empty) return;
        // App compatibility policy: mirror failover can duplicate an ambiguously completed report.
        // The public SDK itself never retries or fails over mutations.
        foreach (var server in await _mirrors.GetServersAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reporter = new RadioBrowserClient(_reportClient, new FixedMirrors([server]), new ClientOptions { UserAgent = _userAgent });
                await reporter.RegisterClickAsync(uuid, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException)
            { AppDiagnostics.Record("directory.play-report", exception); }
        }
    }

    private static async Task<IReadOnlyList<RadioStation>> AdaptAsync(Func<Task<IReadOnlyList<Station>>> read)
    {
        try
        {
            return (await read().ConfigureAwait(false)).Select(station => new RadioStation
            {
                Id = station.StationUuid,
                Name = station.Name,
                StreamUrl = station.ResolvedUrl,
                ArtworkUrl = station.Favicon,
                Tags = station.Tags,
                CountryCode = station.CountryCode,
                Codec = station.Codec,
                Bitrate = station.Bitrate
            }).ToArray();
        }
        catch (DirectoryUnavailableException exception)
        {
            AppDiagnostics.Record("directory.request", exception);
            throw new RadioDirectoryUnavailableException("Radio directory unavailable. Check your connection and try again.", exception);
        }
    }

    private sealed class ProviderAdapter(IRadioBrowserServerProvider provider) : IMirrorProvider
    {
        public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default) => provider.GetServersAsync(cancellationToken);
    }
    private sealed class FixedMirrors : IMirrorProvider
    {
        private readonly Uri[] _servers;
        public FixedMirrors(IReadOnlyList<Uri> servers)
        {
            if (servers.Count == 0 || servers.Any(uri => !uri.IsAbsoluteUri || uri.Scheme != "https"
                || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0))
                throw new ArgumentException("At least one HTTPS directory root is required.", nameof(servers));
            _servers = [.. servers];
        }
        public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Uri>>(Array.AsReadOnly(_servers));
        }
    }
}

public sealed class RadioDirectoryUnavailableException(string message, Exception? innerException)
    : Exception(message, innerException);
