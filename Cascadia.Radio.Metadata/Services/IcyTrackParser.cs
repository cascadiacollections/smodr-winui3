using System.Text;
using smodr.Models;

namespace smodr.Services;

/// <summary>Conservative parser for common ICY and broadcaster cue dialects.</summary>
public static class IcyTrackParser
{
    private static readonly HashSet<string> _singleFieldKeys =
        ["streamtitle", "streamurl", "title", "artist", "album", "text"];
    private static readonly string[] _promoPhrases =
        ["now playing", "listen live", "on air", "follow us", "like us", "text the word",
         "text to win", "call now", "visit us", "check us out", "download our app",
         "commercial break", "stay tuned", "coming up next", "back after this", "brought to you by"];
    private static readonly HashSet<string> _junkWords =
        ["unknown", "stream", "live", "offline", "test", "advertisement"];
    private static readonly string[] _domainSuffixes = [".com", ".org", ".net", ".fm", ".io", ".co"];

    public static RadioTrackInfo? Parse(string? raw, string stationName)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096) return null;
        // Keep leading whitespace: " - Song" means an empty artist in several feeds.
        var info = ParseCore(raw.TrimEnd('\0'), 0);
        return info is null ? null : Accept(info, stationName);
    }

    internal static RadioTrackInfo? Accept(RadioTrackInfo info, string stationName) =>
        IsLikelySong(info, stationName) ? info : null;

    /// <summary>Distinguishes a damaged song cue from empty metadata or advertising.</summary>
    public static bool IsDamagedSongCue(string? raw, string stationName)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096) return false;
        var info = ParseCore(raw.TrimEnd('\0'), 0);
        return info is not null && (HasDamagedText(info.Title)
            || info.Artist is { } artist && HasDamagedText(artist))
            && IsLikelySong(info, stationName, allowDamagedText: true);
    }

    /// <summary>Some broadcasters emit a second cue with title in the artist
    /// field and "artist - album" in the title field for the same song.</summary>
    public static bool IsAlbumEcho(RadioTrackInfo? current, RadioTrackInfo candidate) =>
        current is { Artist: { Length: > 0 } artist }
        && string.Equals(candidate.Artist, current.Title, StringComparison.OrdinalIgnoreCase)
        && candidate.Title.StartsWith($"{artist} - ", StringComparison.OrdinalIgnoreCase);

    private static RadioTrackInfo? ParseCore(string raw, int depth)
    {
        if (depth >= 4 || string.IsNullOrWhiteSpace(raw)) return null;
        if (TryFields(raw, out var fields))
        {
            if (fields.TryGetValue("streamtitle", out var combined)
                || fields.TryGetValue("text", out combined))
                return ParseCore(combined, depth + 1);

            if (fields.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            {
                var artistField = fields.GetValueOrDefault("artist")?.Trim();
                return new RadioTrackInfo(title.Trim(),
                    string.IsNullOrWhiteSpace(artistField) ? null : artistField);
            }
            return null;
        }

        var separator = raw.IndexOf(" - ", StringComparison.Ordinal);
        var artist = separator < 0 ? null : raw[..separator].Trim();
        var titlePart = separator < 0 ? raw.Trim() : raw[(separator + 3)..].Trim();
        if (TryFields(titlePart, out _)) return ParseCore(titlePart, depth + 1);
        if (titlePart.Length == 0 || IsWireSoup(titlePart) || (artist is not null && IsWireSoup(artist)))
            return null;
        if (string.Equals(titlePart, "Spot Block Start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(titlePart, "Spot Block End", StringComparison.OrdinalIgnoreCase))
            return null;
        return new RadioTrackInfo(titlePart, artist is { Length: > 0 } ? artist : null);
    }

    private static bool TryFields(string raw, out Dictionary<string, string> fields)
    {
#pragma warning disable IDE0028 // A collection expression cannot carry the case-insensitive comparer.
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
        var cursor = 0;
        while (cursor < raw.Length)
        {
            SkipSeparators(raw, ref cursor);
            if (cursor == raw.Length) break;
            var keyStart = cursor;
            while (cursor < raw.Length && IsKeyCharacter(raw[cursor])) cursor++;
            var keyEnd = cursor;
            while (cursor < raw.Length && char.IsWhiteSpace(raw[cursor])) cursor++;
            if (keyEnd == keyStart || cursor >= raw.Length || raw[cursor] != '=') return false;
            var key = raw[keyStart..keyEnd].ToLowerInvariant();
            cursor++;
            while (cursor < raw.Length && char.IsWhiteSpace(raw[cursor])) cursor++;
            string value;
            if (cursor < raw.Length && raw[cursor] is '\'' or '"')
            {
                var quote = raw[cursor++];
                var valueStart = cursor;
                while (cursor < raw.Length)
                {
                    if (raw[cursor] == quote && LooksLikeBoundary(raw, cursor + 1)) break;
                    cursor++;
                }
                value = raw[valueStart..cursor];
                if (cursor < raw.Length) cursor++;
            }
            else
            {
                var valueStart = cursor;
                while (cursor < raw.Length)
                {
                    if (raw[cursor] is ';' or ',' && LooksLikeBoundary(raw, cursor + 1)) break;
                    cursor++;
                }
                value = raw[valueStart..cursor].Trim();
            }
            fields[key] = value;
        }

        return fields.Count >= 2 || fields.Keys.Any(_singleFieldKeys.Contains);
    }

    private static bool LooksLikeBoundary(string raw, int index)
    {
        SkipSeparators(raw, ref index);
        if (index == raw.Length) return true;
        var keyStart = index;
        while (index < raw.Length && IsKeyCharacter(raw[index])) index++;
        var hasKey = index > keyStart;
        while (index < raw.Length && char.IsWhiteSpace(raw[index])) index++;
        return hasKey && index < raw.Length && raw[index] == '=';
    }

    private static void SkipSeparators(string raw, ref int index)
    {
        while (index < raw.Length && (raw[index] is ';' or ',' || char.IsWhiteSpace(raw[index]))) index++;
    }

    private static bool IsKeyCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool IsWireSoup(string value) =>
        value.Contains("=\"", StringComparison.Ordinal)
        || value.Contains("='", StringComparison.Ordinal)
        || value.StartsWith("TrackId=", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("StreamUrl=", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelySong(RadioTrackInfo info, string stationName, bool allowDamagedText = false)
    {
        if (info.Title.Length == 0 || info.Title.Length > 300 || info.Artist?.Length > 200) return false;
        foreach (var value in new[] { info.Title, info.Artist }.Where(value => value is not null))
        {
            // Original bytes are unrecoverable once a replacement character is
            // received. Reject damaged text rather than guessing an artist.
            if (!allowDamagedText && HasDamagedText(value!)) return false;
            if (value!.Contains("http://", StringComparison.OrdinalIgnoreCase)
                || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
                || value.Contains("www.", StringComparison.OrdinalIgnoreCase)
                || (!value.Any(char.IsWhiteSpace) && _domainSuffixes.Any(suffix =>
                    value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    || value.Contains($"{suffix}/", StringComparison.OrdinalIgnoreCase)))
                || _promoPhrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (Normalize(value) == Normalize(stationName)) return false;
        }

        if (info.Artist is null && !info.Title.Any(char.IsWhiteSpace)
            && (_junkWords.Contains(info.Title.ToLowerInvariant()) || info.Title.Any(char.IsDigit)))
            return false;
        return true;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private static bool HasDamagedText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\uFFFD' || char.IsControl(character)) return true;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return true;
                index++;
            }
            else if (char.IsLowSurrogate(character)) return true;
        }
        return false;
    }
}
