using System.Text.Json.Serialization;

namespace smodr.Models;

public sealed record TopTrack(string Title, string Artist, int PlayCount, DateTimeOffset LastHeardAt)
{
    [JsonIgnore]
    public string Display => $"{Artist} — {Title}";

    [JsonIgnore]
    public string Details => $"{PlayCount} {(PlayCount == 1 ? "play" : "plays")} · Last heard {LastHeardAt.ToLocalTime():g}";

    /// <summary>List containers announce ToString() to screen readers.</summary>
    public override string ToString() => $"{Display}, {Details}";
}
