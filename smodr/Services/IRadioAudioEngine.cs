using Windows.Media.Playback;

namespace smodr.Services;

internal enum RadioAudioEngineKind { MediaPlayer, AudioGraph }

/// <summary>Decoder lifecycle only. Station metadata, user intent, SMTC and retry policy belong to AudioService.</summary>
internal interface IRadioAudioEngine : IDisposable
{
    RadioAudioEngineKind Kind { get; }
    RadioEqualizerPreset Preset { get; }
    MediaPlaybackState State { get; }
    TimeSpan Duration { get; }
    TimeSpan Position { get; }
    event EventHandler<MediaPlaybackState>? StateChanged;
    event EventHandler? Completed;
    event EventHandler? Failed;
    void Play();
    void Pause();
    void SetVolume(double volume);
}
