using System.Text;
using System.Text.Json;
using smodr.Models;

namespace smodr.Services;

public enum FavoritesImportError
{
    None,
    Empty,
    TooLarge,
    NotJson,
    NotABackup,
    NewerSchemaVersion,
    UnsupportedSchemaVersion,
    TooManyEntries
}

/// <summary>What a validated backup would add; nothing is saved until the user confirms.</summary>
public sealed class FavoritesImportPreview
{
    private FavoritesImportPreview(FavoritesImportError error, IReadOnlyList<RadioStation> stationsToAdd,
        int alreadySavedCount, int duplicateCount, int invalidCount)
    {
        Error = error;
        StationsToAdd = stationsToAdd;
        AlreadySavedCount = alreadySavedCount;
        DuplicateCount = duplicateCount;
        InvalidCount = invalidCount;
    }

    public FavoritesImportError Error { get; }

    /// <summary>New stations in backup order, already de-duplicated against the file and the saved library.</summary>
    public IReadOnlyList<RadioStation> StationsToAdd { get; }

    public int AlreadySavedCount { get; }

    /// <summary>Later repeats of a station that appears earlier in the same file.</summary>
    public int DuplicateCount { get; }

    /// <summary>Entries skipped because a required field was missing, mistyped or unsafe.</summary>
    public int InvalidCount { get; }

    public bool IsValid => Error == FavoritesImportError.None;
    public bool CanImport => IsValid && StationsToAdd.Count > 0;

    public string ErrorMessage => Error switch
    {
        FavoritesImportError.None => string.Empty,
        FavoritesImportError.Empty => "The file is empty.",
        FavoritesImportError.TooLarge => "The file is larger than a favorites backup can be.",
        FavoritesImportError.NewerSchemaVersion =>
            "This backup was made by a newer version of Shoutkit. Update the app and try again.",
        FavoritesImportError.UnsupportedSchemaVersion => "This favorites backup format version is not supported.",
        FavoritesImportError.TooManyEntries =>
            $"The backup has more than {FavoritesTransfer.MaxEntries:N0} stations.",
        _ => "The file is not a Shoutkit favorites backup."
    };

    /// <summary>Plain-language result of validation, suitable for the confirmation dialog.</summary>
    public string Summary
    {
        get
        {
            if (!IsValid)
            {
                return ErrorMessage;
            }

            var parts = new List<string>(4)
            {
                StationsToAdd.Count == 0
                    ? "There are no new stations to add."
                    : $"{Stations(StationsToAdd.Count)} will be added after your current favorites, which stay unchanged."
            };
            if (AlreadySavedCount > 0)
            {
                parts.Add($"{Stations(AlreadySavedCount)} already saved.");
            }

            if (DuplicateCount > 0)
            {
                parts.Add($"{Count(DuplicateCount, "repeated entry", "repeated entries")} in the file ignored.");
            }

            if (InvalidCount > 0)
            {
                parts.Add($"{Count(InvalidCount, "invalid entry", "invalid entries")} skipped.");
            }

            return string.Join(" ", parts);

            static string Stations(int count)
            {
                return Count(count, "station", "stations");
            }

            static string Count(int count, string one, string many)
            {
                return count == 1 ? $"1 {one}" : $"{count:N0} {many}";
            }
        }
    }

    internal static FavoritesImportPreview Failed(FavoritesImportError error)
    {
        return new FavoritesImportPreview(error, [], 0, 0, 0);
    }

    internal static FavoritesImportPreview Create(IReadOnlyList<RadioStation> stationsToAdd, int alreadySavedCount,
        int duplicateCount, int invalidCount)
    {
        return new FavoritesImportPreview(FavoritesImportError.None, stationsToAdd, alreadySavedCount,
            duplicateCount, invalidCount);
    }
}

/// <summary>
///     Favorites backup compatible with Shoutkit iOS <c>FavoritesTransferDocument</c> (schema 1): a
///     <c>favorites</c> array of <c>id</c>, <c>name</c>, <c>streamURL</c>, <c>genre</c>, <c>artworkURL</c> and
///     <c>sortIndex</c>. Windows adds optional <c>tags</c>, <c>countryCode</c>, <c>codec</c> and <c>bitrate</c>,
///     which iOS ignores. Import validates everything before the library is touched and only ever appends.
/// </summary>
public static class FavoritesTransfer
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxEntries = 1_000;
    internal const int MaxNameLength = 200;
    internal const int MaxUrlLength = 2_048;
    internal const int MaxIdLength = 256;
    internal const int MaxTagsLength = 512;
    internal const int MaxCodecLength = 32;
    internal const int MaxBitrate = 10_000;

    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        MaxDepth = 8,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        AllowDuplicateProperties = false
    };

    public static string Export(IReadOnlyList<RadioStation> favorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            // Keys are sorted to match the iOS encoder's output.
            writer.WriteStartObject();
            writer.WriteStartArray("favorites");
            for (var index = 0; index < favorites.Count; index++)
            {
                WriteFavorite(writer, favorites[index], index);
            }

            writer.WriteEndArray();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Reads at most <see cref="MaxFileBytes" /> (plus one to detect overflow) before validating.</summary>
    public static async Task<FavoritesImportPreview> PreviewAsync(Stream utf8, IReadOnlyList<RadioStation> saved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(saved);
        if (utf8.CanSeek && utf8.Length - utf8.Position > MaxFileBytes)
        {
            return FavoritesImportPreview.Failed(FavoritesImportError.TooLarge);
        }

        var buffer = new byte[MaxFileBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length
               && (read = await utf8.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false)) != 0)
        {
            length += read;
        }

        return Preview(buffer.AsMemory(0, length), saved);
    }

    /// <summary>Validates a backup and plans the merge without mutating <paramref name="saved" />.</summary>
    public static FavoritesImportPreview Preview(ReadOnlyMemory<byte> utf8, IReadOnlyList<RadioStation> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (utf8.Length > MaxFileBytes)
        {
            return FavoritesImportPreview.Failed(FavoritesImportError.TooLarge);
        }

        if (utf8.Span.StartsWith(Encoding.UTF8.Preamble))
        {
            utf8 = utf8[Encoding.UTF8.Preamble.Length..];
        }

        if (utf8.Span.Trim(" \t\r\n"u8).IsEmpty)
        {
            return FavoritesImportPreview.Failed(FavoritesImportError.Empty);
        }

        JsonDocument document;
        try { document = JsonDocument.Parse(utf8, _documentOptions); }
        catch (JsonException) { return FavoritesImportPreview.Failed(FavoritesImportError.NotJson); }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("favorites", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return FavoritesImportPreview.Failed(FavoritesImportError.NotABackup);
            }

            if (!version.TryGetInt32(out var schema) || schema != CurrentSchemaVersion)
            {
                return FavoritesImportPreview.Failed(schema > CurrentSchemaVersion
                    ? FavoritesImportError.NewerSchemaVersion
                    : FavoritesImportError.UnsupportedSchemaVersion);
            }

            if (entries.GetArrayLength() > MaxEntries)
            {
                return FavoritesImportPreview.Failed(FavoritesImportError.TooManyEntries);
            }

            var parsed = new List<(RadioStation Station, long Order, int Offset)>();
            var invalid = 0;
            var offset = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (TryReadFavorite(entry, offset, out var station, out var order))
                {
                    parsed.Add((station, order, offset));
                }
                else
                {
                    invalid++;
                }

                offset++;
            }

            // Like iOS, honour sortIndex and fall back to file order for ties.
            var ordered = parsed.OrderBy(item => item.Order).ThenBy(item => item.Offset)
                .Select(item => item.Station).ToList();
            var unique = new List<RadioStation>(ordered.Count);
            foreach (var station in ordered)
            {
                if (!unique.Exists(item => RadioStationIdentity.Matches(item, station)))
                {
                    unique.Add(station);
                }
            }

            var additions = SelectNew(saved, unique);
            return FavoritesImportPreview.Create(additions, unique.Count - additions.Count,
                ordered.Count - unique.Count, invalid);
        }
    }

    /// <summary>Stations from <paramref name="incoming" /> that are neither saved nor repeated earlier in it.</summary>
    public static IReadOnlyList<RadioStation> SelectNew(IReadOnlyList<RadioStation> saved,
        IReadOnlyList<RadioStation> incoming)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(incoming);
        var result = new List<RadioStation>();
        foreach (var station in incoming)
        {
            if (!saved.Any(item => RadioStationIdentity.Matches(item, station))
                && !result.Exists(item => RadioStationIdentity.Matches(item, station)))
            {
                result.Add(station);
            }
        }

        return result.AsReadOnly();
    }

    private static void WriteFavorite(Utf8JsonWriter writer, RadioStation station, int index)
    {
        var tags = station.Tags ?? string.Empty;
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(station.ArtworkUrl))
        {
            writer.WriteString("artworkURL", station.ArtworkUrl);
        }

        if (station.Bitrate > 0)
        {
            writer.WriteNumber("bitrate", station.Bitrate);
        }

        if (!string.IsNullOrWhiteSpace(station.Codec))
        {
            writer.WriteString("codec", station.Codec);
        }

        if (!string.IsNullOrWhiteSpace(station.CountryCode))
        {
            writer.WriteString("countryCode", station.CountryCode);
        }

        writer.WriteString("genre",
            tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty);
        // iOS requires a non-empty id; a station without a directory id is identified by its stream.
        writer.WriteString("id", string.IsNullOrWhiteSpace(station.Id) ? station.StreamUrl : station.Id);
        writer.WriteString("name", station.Name);
        writer.WriteNumber("sortIndex", index);
        writer.WriteString("streamURL", station.StreamUrl);
        if (!string.IsNullOrWhiteSpace(tags))
        {
            writer.WriteString("tags", tags);
        }

        writer.WriteEndObject();
    }

    private static bool TryReadFavorite(JsonElement entry, int offset, out RadioStation station, out long order)
    {
        station = null!;
        order = offset;
        if (entry.ValueKind != JsonValueKind.Object
            || !TryReadString(entry, "id", MaxIdLength, out var id)
            || !TryReadString(entry, "name", MaxNameLength, out var name)
            || !TryReadString(entry, "streamURL", MaxUrlLength, out var stream)
            || !TryReadString(entry, "genre", MaxTagsLength, out var genre)
            || !TryReadString(entry, "tags", MaxTagsLength, out var tags)
            || !TryReadString(entry, "artworkURL", MaxUrlLength, out var artwork)
            || !TryReadString(entry, "countryCode", 8, out var country)
            || !TryReadString(entry, "codec", MaxCodecLength, out var codec)
            || !TryReadInteger(entry, "bitrate", out var bitrate)
            || !TryReadInteger(entry, "sortIndex", out var sortIndex))
        {
            return false;
        }

        // A favorite must be nameable and playable without another directory lookup.
        if (name.Length == 0 || !IsSafeWebUrl(stream))
        {
            return false;
        }

        order = sortIndex ?? offset;
        station = new RadioStation
        {
            Id = string.Equals(id, stream, StringComparison.Ordinal) ? string.Empty : id,
            Name = name,
            StreamUrl = stream,
            // Artwork and descriptive fields are cosmetic: an unusable value is dropped, not fatal.
            ArtworkUrl = IsSafeWebUrl(artwork) ? artwork : string.Empty,
            Tags = tags.Length != 0 ? tags : genre,
            CountryCode = country.Length == 2 && country.All(char.IsAsciiLetter)
                ? country.ToUpperInvariant()
                : string.Empty,
            Codec = codec,
            Bitrate = bitrate is > 0 and <= MaxBitrate ? (int)bitrate : 0
        };
        return true;
    }

    /// <summary>Absent and null read as empty; any other non-string, overlong or control-bearing value fails.</summary>
    private static bool TryReadString(JsonElement entry, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!entry.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString()!.Trim();
        if (text.Length > maxLength || text.Any(char.IsControl))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadInteger(JsonElement entry, string name, out long? value)
    {
        value = null;
        if (!entry.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var number))
        {
            return false;
        }

        value = number;
        return true;
    }

    private static bool IsSafeWebUrl(string value)
    {
        return value.Length != 0
               && Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https"
               && uri.UserInfo.Length == 0
               && !string.IsNullOrEmpty(uri.Host);
    }
}
