using smodr.Models;
using Windows.Media.Playback;

namespace smodr.Services;

public interface IRadioPlayer
{
    RadioStation? CurrentStation { get; }
    RadioTrackInfo? CurrentTrack { get; }
    bool IsPlaybackRequested { get; }
    event EventHandler<RadioStation?>? StationChanged;
    event EventHandler<RadioTrackUpdate?>? TrackChanged;
    event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
    event EventHandler<string>? PlaybackFailed;
    event EventHandler? UserPlaybackStarted;
    Task PlayStationAsync(RadioStation station);
    void Play();
    void Pause();
    void StopStation();
    void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl);
}
