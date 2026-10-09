using System.Globalization;

namespace smodr.Models;

/// <summary>A bounded, untrusted station payload from Windows protocol activation.</summary>
public sealed record StationLaunchLink(RadioStation Station, bool AutoPlay)
{
    private static readonly HashSet<string> _allowedKeys = new(
    [
        "id", "name", "genre", "listenerCount", "autoPlay", "presentNowPlaying",
        "bitrate", "artworkURL", "streamURL", "tags", "country", "codec",
        "language", "clickTrend", "votes"
    ], StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(Uri? uri, out StationLaunchLink? link)
    {
        link = null;
        if (uri is null || !uri.IsAbsoluteUri || uri.OriginalString.Length > 4096
            || !uri.Scheme.Equals("holmdel", StringComparison.OrdinalIgnoreCase)
            || uri.Host is not ("station" or "play")
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Port != -1
            || uri.Fragment.Length != 0)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in uri.Query.TrimStart('?').Split('&'))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return false;
            }

            var key = Uri.UnescapeDataString(item[..separator]);
            var value = Uri.UnescapeDataString(item[(separator + 1)..]).Trim();
            if (!_allowedKeys.Contains(key) || value.Any(char.IsControl)
                                            || !values.TryAdd(key, value))
            {
                return false;
            }
        }

        if (!values.TryGetValue("streamURL", out var streamValue)
            || !TryHttpsUrl(streamValue, out var stream))
        {
            return false;
        }

        var id = values.GetValueOrDefault("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = stream.AbsoluteUri;
        }

        var name = values.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = id;
        }

        if (id.Length > 256 || name.Length > 128)
        {
            return false;
        }

        var autoPlay = true;
        if (values.TryGetValue("autoPlay", out var autoPlayValue))
        {
            if (autoPlayValue.Equals("0", StringComparison.Ordinal)
                || autoPlayValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                autoPlay = false;
            }
            else if (!autoPlayValue.Equals("1", StringComparison.Ordinal)
                     && !autoPlayValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var artwork = string.Empty;
        if (values.TryGetValue("artworkURL", out var artworkValue))
        {
            if (!TryHttpsUrl(artworkValue, out var artworkUri))
            {
                return false;
            }

            artwork = artworkUri.AbsoluteUri;
        }

        var bitrate = 0;
        if (values.TryGetValue("bitrate", out var bitrateValue)
            && (!int.TryParse(bitrateValue, NumberStyles.None, CultureInfo.InvariantCulture, out bitrate)
                || bitrate > 10_000))
        {
            return false;
        }

        link = new StationLaunchLink(
            new RadioStation
            {
                Id = id,
                Name = name,
                StreamUrl = stream.AbsoluteUri,
                ArtworkUrl = artwork,
                Tags = values.GetValueOrDefault("tags") ?? values.GetValueOrDefault("genre") ?? string.Empty,
                Country = values.GetValueOrDefault("country") ?? string.Empty,
                Codec = values.GetValueOrDefault("codec") ?? string.Empty,
                Bitrate = bitrate
            }, autoPlay);
        return true;
    }

    private static bool TryHttpsUrl(string value, out Uri uri)
    {
        uri = null!;
        if (value.Length is < 1 or > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                                          || parsed.Scheme != Uri.UriSchemeHttps || parsed.IsLoopback
                                          || parsed.UserInfo.Length != 0 || parsed.Host.Length == 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
