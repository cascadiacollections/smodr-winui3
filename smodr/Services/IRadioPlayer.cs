using smodr.Models;
using Windows.Media.Playback;

namespace smodr.Services;

public interface IRadioPlayer
{
    RadioStation? CurrentStation { get; }
    bool IsPlaying { get; }
    bool IsPlaybackRequested { get; }
    event EventHandler<RadioStation?>? StationChanged;
    event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
    event EventHandler<string>? PlaybackFailed;
    Task PlayStationAsync(RadioStation station);
    void Play();
    void Pause();
    void StopStation();
}
