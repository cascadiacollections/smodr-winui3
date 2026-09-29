using smodr.Models;

namespace smodr.Services;

internal readonly record struct NowPlayingMetadata(string Title, string Artist, string AlbumTitle)
{
    public static NowPlayingMetadata ForStation(RadioStation station) => new(
        string.IsNullOrWhiteSpace(station.Name) ? "Live Radio" : station.Name.Trim(),
        string.IsNullOrWhiteSpace(station.Details) ? "Live Radio" : station.Details,
        "Shoutkit");

    public static NowPlayingMetadata ForEpisode(Episode episode) => new(
        string.IsNullOrWhiteSpace(episode.Title) ? "Podcast" : episode.Title.Trim(),
        "Kevin Smith & Scott Mosier",
        "SModcast");
}
