using System.Text.Json.Serialization;

namespace smodr.Models;

public sealed class HeardTrack
{
    public string StationId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public DateTimeOffset HeardAt { get; init; }

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";

    [JsonIgnore]
    public string Details => $"{StationName} · {HeardAt.ToLocalTime():g}";
}
