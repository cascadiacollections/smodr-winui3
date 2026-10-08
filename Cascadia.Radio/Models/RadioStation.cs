using System.Globalization;
using System.Text.Json.Serialization;

namespace smodr.Models;

public sealed class RadioStation
{
    [JsonPropertyName("stationuuid")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url_resolved")]
    public string StreamUrl { get; init; } = string.Empty;

    [JsonPropertyName("favicon")]
    public string ArtworkUrl { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = string.Empty;

    [JsonPropertyName("countrycode")]
    public string CountryCode { get; init; } = string.Empty;

    [JsonIgnore]
    public string CountryDisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CountryCode)) return Country ?? string.Empty;
            var code = CountryCode.Trim().ToUpperInvariant();
            if (code.Length != 2 || !code.All(char.IsAsciiLetter)) return string.Empty;
            try { return new RegionInfo(code).DisplayName; }
            catch (ArgumentException) { return code; }
        }
    }

    [JsonPropertyName("codec")]
    public string Codec { get; init; } = string.Empty;

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; init; }

    public string Details => string.Join(" · ", new[]
    {
        CountryDisplayName,
        (Tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty,
        Bitrate > 0 ? $"{Bitrate} kbps {Codec}" : Codec ?? string.Empty
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>List containers announce ToString() to screen readers.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Unnamed station" : Name;
}
