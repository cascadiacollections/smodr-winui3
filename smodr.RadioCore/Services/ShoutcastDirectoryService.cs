using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using smodr.Models;

namespace smodr.Services;

public interface IStationStreamResolver
{
    Task<RadioStation> ResolveAsync(RadioStation station, CancellationToken cancellationToken = default);
}

/// <summary>Opt-in key-gated directory; keys are never included in saved station URLs.</summary>
public sealed class ShoutcastDirectoryService(HttpClient client, string apiKey) : IRadioDirectoryService, IStationStreamResolver
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private static readonly Uri _directory = new("https://api.shoutcast.com/legacy/");
    private static readonly Uri _tuneIn = new("https://yp.shoutcast.com/sbin/tunein-station.pls");

    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        QueryAsync("Top500", null, null, limit, cancellationToken);
    public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(query) ? GetPopularStationsAsync(limit, cancellationToken)
            : QueryAsync("stationsearch", "search", query.Trim(), limit, cancellationToken);
    public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(genre) ? GetPopularStationsAsync(limit, cancellationToken)
            : QueryAsync("genresearch", "genre", genre.Trim(), limit, cancellationToken);

    private async Task<IReadOnlyList<RadioStation>> QueryAsync(string endpoint, string? field, string? value, int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 512 || apiKey.Any(char.IsControl))
            throw new RadioDirectoryUnavailableException("SHOUTcast requires a valid configured developer key.", null);
        var query = "?k=" + Uri.EscapeDataString(apiKey)
            + (field is null ? string.Empty : "&" + field + "=" + Uri.EscapeDataString(value!));
        try
        {
            var body = await ReadAsync(new Uri(_directory, endpoint + query), 2 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2 * 1024 * 1024,
            });
            var document = XDocument.Load(reader);
            var stations = new List<RadioStation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.Descendants("station").Take(1000))
            {
                var id = (string?)element.Attribute("id");
                var name = (string?)element.Attribute("name");
                if (id is null || id.Length is < 1 or > 20 || !id.All(char.IsAsciiDigit)
                    || string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl) || !seen.Add(id)) continue;
                _ = int.TryParse((string?)element.Attribute("br"), NumberStyles.None, CultureInfo.InvariantCulture, out var bitrate);
                stations.Add(new RadioStation
                {
                    Id = "shoutcast:" + id,
                    Name = name,
                    StreamUrl = new Uri(_tuneIn, "?id=" + id).AbsoluteUri,
                    Tags = (string?)element.Attribute("genre") ?? string.Empty,
                    Codec = (string?)element.Attribute("mt") ?? string.Empty,
                    Bitrate = Math.Clamp(bitrate, 0, 10000),
                });
                if (stations.Count == Math.Clamp(limit, 1, 100)) break;
            }
            return stations;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or XmlException or OperationCanceledException or DecoderFallbackException)
        {
            // Transport exception messages can contain a key-bearing request URL.
            throw new RadioDirectoryUnavailableException("SHOUTcast unavailable. Check the developer-key configuration and connection.",
                new InvalidOperationException(exception.GetType().Name));
        }
    }

    public async Task<RadioStation> ResolveAsync(RadioStation station, CancellationToken cancellationToken = default)
    {
        if (!station.Id.StartsWith("shoutcast:", StringComparison.Ordinal)) return station;
        var id = station.Id[10..];
        if (id.Length is < 1 or > 20 || !id.All(char.IsAsciiDigit)) throw new InvalidDataException("Invalid SHOUTcast station identity.");
        var playlist = await ReadAsync(new Uri(_tuneIn, "?id=" + id), 32 * 1024, cancellationToken).ConfigureAwait(false);
        foreach (var line in playlist.Split('\n'))
        {
            var entry = line.Trim();
            if (!entry.StartsWith("File", StringComparison.OrdinalIgnoreCase)) continue;
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 4 || !entry.AsSpan(4, equals - 4).ToString().All(char.IsAsciiDigit)) continue;
            if (!Uri.TryCreate(entry[(equals + 1)..].Trim(), UriKind.Absolute, out var stream)
                || stream.Scheme is not ("http" or "https") || stream.UserInfo.Length != 0 || stream.IsLoopback) continue;
            return new RadioStation
            {
                Id = station.Id,
                Name = station.Name,
                StreamUrl = stream.AbsoluteUri,
                ArtworkUrl = station.ArtworkUrl,
                Tags = station.Tags,
                CountryCode = station.CountryCode,
                Country = station.Country,
                Codec = station.Codec,
                Bitrate = station.Bitrate,
            };
        }
        throw new InvalidDataException("SHOUTcast did not return a supported stream.");
    }

    private async Task<string> ReadAsync(Uri uri, int maximum, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"ShoutkitWindows/{typeof(ShoutcastDirectoryService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Directory response exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException("Directory response exceeds size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
        }
        return _strictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

public sealed class FallbackRadioDirectory(IRadioDirectoryService primary, IRadioDirectoryService fallback) : IRadioDirectoryService
{
    public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        QueryAsync(service => service.GetPopularStationsAsync(limit, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) =>
        QueryAsync(service => service.SearchAsync(query, limit, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50, CancellationToken cancellationToken = default) =>
        QueryAsync(service => service.SearchGenreAsync(genre, limit, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<RadioStation>> QueryAsync(Func<IRadioDirectoryService, Task<IReadOnlyList<RadioStation>>> query,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await query(primary).ConfigureAwait(false);
            if (results.Count != 0) return results;
        }
        catch (RadioDirectoryUnavailableException) { }
        cancellationToken.ThrowIfCancellationRequested();
        return await query(fallback).ConfigureAwait(false);
    }
}
