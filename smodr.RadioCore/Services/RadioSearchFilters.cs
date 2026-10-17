using System.Globalization;
using Cascadia.RadioBrowser;
using smodr.Models;

namespace smodr.Services;

/// <summary>Normalized directory search filters (iOS <c>StationSearchFilters</c> minus the bitrate ceiling).</summary>
public sealed record RadioSearchFilters
{
    public const int MaxTagLength = 64;
    public const int MaxBitrate = 10_000;
    public static readonly RadioSearchFilters None = new();

    private RadioSearchFilters() { }

    /// <summary>Kilobits per second; stations reporting a lower known bitrate are excluded.</summary>
    public int? MinimumBitrate { get; private init; }

    /// <summary>Lower-case tag text; Radio Browser matches it within a station's tag list.</summary>
    public string? Tag { get; private init; }

    /// <summary>ISO 3166-1 alpha-2, upper case.</summary>
    public string? CountryCode { get; private init; }

    public bool IsActive => MinimumBitrate is not null || Tag is not null || CountryCode is not null;

    /// <summary>Drops out-of-range or malformed values rather than sending them to the directory.</summary>
    public static RadioSearchFilters Create(int? minimumBitrate = null, string? tag = null,
        string? countryCode = null)
    {
        var normalizedTag = NormalizeTag(tag);
        var normalizedCountry = countryCode?.Trim().ToUpperInvariant();
        return new RadioSearchFilters
        {
            MinimumBitrate = minimumBitrate is > 0 ? Math.Min(minimumBitrate.Value, MaxBitrate) : null,
            Tag = normalizedTag,
            CountryCode = normalizedCountry is { Length: 2 } && normalizedCountry.All(char.IsAsciiLetterUpper)
                ? normalizedCountry
                : null
        };
    }

    public RadioSearchFilters WithTag(string? tag)
    {
        return Create(MinimumBitrate, tag, CountryCode);
    }

    public SearchOptions ToSearchOptions(string? name, int limit)
    {
        return new SearchOptions
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Tag = Tag,
            CountryCode = CountryCode,
            BitrateMin = MinimumBitrate,
            Limit = Math.Clamp(limit, 1, 100)
        };
    }

    /// <summary>
    ///     Client-side equivalent for directories that cannot filter. As on iOS, an unknown bitrate or country does not
    ///     exclude a station; a tag filter requires a matching tag.
    /// </summary>
    public bool Matches(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        return (MinimumBitrate is not { } minimum || station.Bitrate <= 0 || station.Bitrate >= minimum)
               && (Tag is null || (station.Tags ?? string.Empty).Contains(Tag, StringComparison.OrdinalIgnoreCase))
               && (CountryCode is null || string.IsNullOrWhiteSpace(station.CountryCode)
                                       || string.Equals(station.CountryCode.Trim(), CountryCode,
                                           StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Canonical text for a snapshot cache key; hash it before use, as with plain search text.</summary>
    public string CacheIdentity(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return string.Join('\u001f', query.Trim().ToLowerInvariant(),
            MinimumBitrate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, Tag ?? string.Empty,
            CountryCode ?? string.Empty);
    }

    private static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var visible = new string([.. tag.Where(character => !char.IsControl(character))]);
        var collapsed = string.Join(' ', visible.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxTagLength)
        {
            collapsed = collapsed[..MaxTagLength].TrimEnd();
        }

        return collapsed.Length == 0 ? null : collapsed.ToLowerInvariant();
    }
}
