using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using smodr.Models;

namespace smodr.Services;

/// <summary>Best-effort song artwork and store-page lookup. Never participates in playback.</summary>
public sealed class AlbumArtworkLookup(HttpClient client) : IAlbumArtworkLookup
{
    private const int MaxResponseBytes = 64 * 1024;
    private const int MaxCacheEntries = 256;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AlbumArtworkMatch?> _cache = [];
    private readonly Dictionary<string, Task<AlbumArtworkMatch?>> _inFlight = [];

    public async Task<AlbumArtworkMatch?> FindAsync(RadioTrackInfo track,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(track.Artist) || string.IsNullOrWhiteSpace(track.Title)) return null;
        var artist = track.Artist.Trim();
        var title = track.Title.Trim();
        var country = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        var key = $"{artist.ToUpperInvariant()}|{title.ToUpperInvariant()}|{country}";
        TaskCompletionSource<AlbumArtworkMatch?>? completion = null;
        Task<AlbumArtworkMatch?> pending;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            if (!_inFlight.TryGetValue(key, out pending!))
            {
                completion = new TaskCompletionSource<AlbumArtworkMatch?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                pending = completion.Task;
                _inFlight[key] = pending;
            }
        }

        // The request belongs to the shared lookup, not one UI listener. A
        // canceled listener stops waiting; another listener can reuse it.
        if (completion is not null) _ = FetchAndCompleteAsync(key, artist, title, country, completion);
        try { return await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
    }

    private async Task FetchAndCompleteAsync(string key, string artist, string title, string country,
        TaskCompletionSource<AlbumArtworkMatch?> completion)
    {
        LookupResult result;
        try { result = await FetchAsync(artist, title, country).ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppDiagnostics.Record("artwork.lookup", exception);
            result = new LookupResult(false, null);
        }

        lock (_gate)
        {
            _inFlight.Remove(key);
            if (result.Cacheable)
            {
                if (_cache.Count >= MaxCacheEntries) _cache.Clear();
                _cache[key] = result.Match;
            }
        }
        completion.TrySetResult(result.Match);
    }

    private async Task<LookupResult> FetchAsync(string artist, string title, string country)
    {
        var query = Uri.EscapeDataString($"{artist} {title}");
        var uri = new Uri($"https://itunes.apple.com/search?term={query}&media=music&entity=song&limit=5&country={Uri.EscapeDataString(country)}");
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength > MaxResponseBytes) return new(false, null);
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaxResponseBytes) return new(false, null);
                await buffer.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
            }

            buffer.Position = 0;
            using var document = await JsonDocument.ParseAsync(buffer).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array) return new(false, null);
            var expectedArtist = Normalize(artist);
            var expectedTitle = Normalize(title);
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !ReadString(item, "artistName", out var foundArtist)
                    || !ReadString(item, "trackName", out var foundTitle)
                    || Normalize(foundArtist) != expectedArtist || Normalize(foundTitle) != expectedTitle
                    || !ReadUri(item, "artworkUrl100", "mzstatic.com", out var artwork)
                    || !ReadStoreUri(item, out var store)) continue;
                var resized = Regex.Replace(artwork.AbsoluteUri, @"/100x100bb(?=\.)", "/600x600bb",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return new(true, new AlbumArtworkMatch(new Uri(resized), store));
            }
            return new(true, null); // A valid response with no exact song is a cacheable miss.
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            or IOException or JsonException)
        {
            return new(false, null); // Network and malformed responses may be retried.
        }
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool ReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool ReadUri(JsonElement item, string name, string host, out Uri uri)
    {
        uri = null!;
        if (!ReadString(item, name, out var raw)
            || !Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || (!parsed.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                && !parsed.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase))) return false;
        uri = parsed;
        return true;
    }

    private static bool ReadStoreUri(JsonElement item, out Uri uri)
    {
        uri = null!;
        if (!ReadString(item, "trackViewUrl", out var raw)
            || !Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || (!parsed.Host.Equals("itunes.apple.com", StringComparison.OrdinalIgnoreCase)
                && !parsed.Host.Equals("music.apple.com", StringComparison.OrdinalIgnoreCase))) return false;
        uri = parsed;
        return true;
    }

    private readonly record struct LookupResult(bool Cacheable, AlbumArtworkMatch? Match);
}
