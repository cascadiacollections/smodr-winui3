using smodr.Models;

namespace smodr.Services;

internal readonly record struct NowPlayingMetadata(string Title, string Artist, string AlbumTitle)
{
    public static NowPlayingMetadata ForPlayback(RadioStation station, RadioTrackInfo? track)
    {
        return track is null
            ? ForStation(station)
            : new NowPlayingMetadata(track.Title, track.Artist ?? station.Name, station.Name);
    }

    public static NowPlayingMetadata ForStation(RadioStation station)
    {
        return new NowPlayingMetadata(
            string.IsNullOrWhiteSpace(station.Name) ? "Live Radio" : station.Name.Trim(),
            string.IsNullOrWhiteSpace(station.Details) ? "Live Radio" : station.Details,
            "Shoutkit");
    }
}
