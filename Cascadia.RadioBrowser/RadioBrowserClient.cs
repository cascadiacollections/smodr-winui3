using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cascadia.RadioBrowser;

/// <summary>Does not own supplied HTTP clients or mirrors. Reads fail over; mutations do not.</summary>
public sealed class RadioBrowserClient
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxRows = 1_000;
    private readonly IClientDiagnostics? _diagnostics;
    private readonly IMirrorProvider _mirrors;
    private readonly ClientOptions _options;
    private readonly HttpClient _reads;
    private readonly HttpClient _writes;

    public RadioBrowserClient(HttpClient reads, IMirrorProvider mirrors, ClientOptions options,
        HttpClient? writes = null, IClientDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(mirrors);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserAgent);
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        // Validate once without mutating the caller's shared default headers.
        using var validation = new HttpRequestMessage();
        validation.Headers.UserAgent.ParseAdd(options.UserAgent);
        _reads = reads;
        _writes = writes ?? reads;
        _mirrors = mirrors;
        _options = options;
        _diagnostics = diagnostics;
    }

    public Task<IReadOnlyList<Station>> SearchAsync(SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidatePage(options.Limit, options.Offset);
        if (options.BitrateMin < 0 || options.BitrateMax < 0 || options.BitrateMin > options.BitrateMax)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var parameters = new List<string>();
        Add(parameters, "name", options.Name);
        Add(parameters, "countrycode", options.CountryCode);
        Add(parameters, "tag", options.Tag);
        Add(parameters, "language", options.Language);
        Add(parameters, "codec", options.Codec);
        Add(parameters, "bitrateMin", options.BitrateMin?.ToString(CultureInfo.InvariantCulture));
        Add(parameters, "bitrateMax", options.BitrateMax?.ToString(CultureInfo.InvariantCulture));
        parameters.Add(Page(options.Limit, options.Offset, options.HideBroken));
        parameters.Add($"order={Order(options.Order)}&reverse={Bool(options.Reverse)}");
        return ReadStationsAsync($"json/stations/search?{string.Join('&', parameters)}", options.Limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<Station>> GetRankedAsync(StationRanking ranking = StationRanking.Popular,
        int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        ValidatePage(limit, offset);
        var path = ranking switch
        {
            StationRanking.Popular => "topclick",
            StationRanking.Votes => "topvote",
            StationRanking.RecentlyClicked => "lastclick",
            StationRanking.RecentlyChanged => "lastchange",
            _ => throw new ArgumentOutOfRangeException(nameof(ranking))
        };
        return ReadStationsAsync($"json/stations/{path}/{limit}?{Page(limit, offset, true)}", limit, cancellationToken);
    }

    public Task<IReadOnlyList<Station>> GetByTagAsync(string tag, int limit = 50, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ValidatePage(limit, offset);
        return ReadStationsAsync(
            $"json/stations/bytag/{Uri.EscapeDataString(tag.Trim())}?{Page(limit, offset, true)}&order=clickcount&reverse=true",
            limit, cancellationToken);
    }

    public Task<IReadOnlyList<Station>> GetByUuidAsync(Guid stationUuid, CancellationToken cancellationToken = default)
    {
        ValidateUuid(stationUuid);
        return ReadStationsAsync($"json/stations/byuuid?uuids={stationUuid:D}", 1, cancellationToken);
    }

    public Task<IReadOnlyList<Station>> GetByUrlAsync(Uri streamUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        if (!streamUrl.IsAbsoluteUri || streamUrl.Scheme is not ("http" or "https") || streamUrl.UserInfo.Length != 0)
        {
            throw new ArgumentException("An absolute HTTP(S) stream URL without credentials is required.",
                nameof(streamUrl));
        }

        return ReadStationsAsync($"json/stations/byurl?url={Uri.EscapeDataString(streamUrl.AbsoluteUri)}", 100,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryValue>> GetValuesAsync(DirectoryFacet facet,
        CancellationToken cancellationToken = default)
    {
        var path = facet switch
        {
            DirectoryFacet.CountryCodes => "countrycodes",
            DirectoryFacet.Languages => "languages",
            DirectoryFacet.Tags => "tags",
            DirectoryFacet.Codecs => "codecs",
            _ => throw new ArgumentOutOfRangeException(nameof(facet))
        };
        return await ReadAsync($"json/{path}?limit=1000", bytes =>
        {
            var values = JsonSerializer.Deserialize(bytes.Span, RadioBrowserJsonContext.Default.DirectoryValueArray)
                         ?? throw new JsonException();
            if (values.Length > MaxRows)
            {
                throw new InvalidDataException("Too many directory values.");
            }

            return (IReadOnlyList<DirectoryValue>)Array.AsReadOnly(values);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Only call for an explicit user play action with consent. Never automatically retried.</summary>
    public Task RegisterClickAsync(Guid stationUuid, CancellationToken cancellationToken = default)
    {
        return MutateAsync("url", stationUuid, cancellationToken);
    }

    /// <summary>Explicit user vote; never automatically retried or sent by station lookup.</summary>
    public Task VoteAsync(Guid stationUuid, CancellationToken cancellationToken = default)
    {
        return MutateAsync("vote", stationUuid, cancellationToken);
    }

    private async Task MutateAsync(string action, Guid uuid, CancellationToken token)
    {
        ValidateUuid(uuid);
        token.ThrowIfCancellationRequested();
        var servers = await _mirrors.GetServersAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (servers.Count == 0)
        {
            throw new DirectoryUnavailableException();
        }

        var bytes = await SendAsync(_writes, servers[0], $"json/{action}/{uuid:D}", 16 * 1024, token)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("ok", out var ok)
            || !(ok.ValueKind == JsonValueKind.True || (ok.ValueKind == JsonValueKind.String
                                                        && string.Equals(ok.GetString(), "true",
                                                            StringComparison.OrdinalIgnoreCase))))
        {
            Diagnose(ClientDiagnostic.ReportRejected);
            throw new InvalidDataException("Radio Browser rejected the explicit action.");
        }
    }

    private Task<IReadOnlyList<Station>> ReadStationsAsync(string path, int limit, CancellationToken token)
    {
        return ReadAsync(path, bytes =>
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() > MaxRows)
            {
                throw new InvalidDataException("Invalid or excessive station response.");
            }

            var result = new List<Station>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var station = item.Deserialize(RadioBrowserJsonContext.Default.Station);
                if (station is null || string.IsNullOrWhiteSpace(station.Name)
                                    || !Uri.TryCreate(station.ResolvedUrl, UriKind.Absolute, out var url)
                                    || url.Scheme is not ("http" or "https"))
                {
                    continue;
                }

                var identity = string.IsNullOrWhiteSpace(station.StationUuid)
                    ? station.ResolvedUrl
                    : station.StationUuid;
                if (seen.Add(identity) && result.Count < limit)
                {
                    result.Add(station);
                }
            }

            return (IReadOnlyList<Station>)result.AsReadOnly();
        }, token);
    }

    private async Task<T> ReadAsync<T>(string path, Func<ReadOnlyMemory<byte>, T> parse, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var server in await _mirrors.GetServersAsync(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            try { return parse(await SendAsync(_reads, server, path, MaxBytes, token).ConfigureAwait(false)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                                  or JsonException or OperationCanceledException)
            {
                Diagnose(ClientDiagnostic.MirrorFailed);
            }
        }

        throw new DirectoryUnavailableException();
    }

    private async Task<ReadOnlyMemory<byte>> SendAsync(HttpClient client, Uri server, string path, int maxBytes,
        CancellationToken token)
    {
        ValidateServer(server);
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_options.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, path));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException("Response size limit exceeded.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException("Response size limit exceeded.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
        }

        if (response.Content.Headers.ContentLength is long length && buffer.Length != length)
        {
            throw new InvalidDataException("Response length mismatch.");
        }

        return buffer.ToArray();
    }

    private void Diagnose(ClientDiagnostic diagnostic)
    {
        try { _diagnostics?.Record(diagnostic); }
        catch (Exception)
        {
            /* Observability must not affect client behavior. */
        }
    }

    internal static void ValidateServer(Uri server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!server.IsAbsoluteUri || server.Scheme != "https" || server.UserInfo.Length != 0
            || server.AbsolutePath != "/" || server.Query.Length != 0 || server.Fragment.Length != 0)
        {
            throw new ArgumentException("An HTTPS server root without credentials is required.", nameof(server));
        }
    }

    private static void ValidateUuid(Guid uuid)
    {
        if (uuid == Guid.Empty)
        {
            throw new ArgumentException("A non-empty station UUID is required.", nameof(uuid));
        }
    }

    private static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 100 || offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }

    private static string Page(int limit, int offset, bool hideBroken)
    {
        return
            $"limit={limit.ToString(CultureInfo.InvariantCulture)}&offset={offset.ToString(CultureInfo.InvariantCulture)}&hidebroken={Bool(hideBroken)}";
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Order(StationOrder order)
    {
        return order switch
        {
            StationOrder.Name => "name",
            StationOrder.ClickCount => "clickcount",
            StationOrder.Votes => "votes",
            StationOrder.Bitrate => "bitrate",
            StationOrder.Random => "random",
            _ => throw new ArgumentOutOfRangeException(nameof(order))
        };
    }

    private static void Add(List<string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
