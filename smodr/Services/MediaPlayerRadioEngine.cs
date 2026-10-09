using Windows.Media.Core;
using Windows.Media.Playback;

namespace smodr.Services;

/// <summary>Owns MediaPlayer and its MediaSource, including a transferred prepared source.</summary>
internal sealed class MediaPlayerRadioEngine : IRadioAudioEngine
{
    private int _disposed;
    private MediaSource? _source;

    public MediaPlayerRadioEngine(PreparedRadioSource? prepared = null)
    {
        if (prepared is not null)
        {
            (Player, _source) = prepared.Transfer();
        }
        else
        {
            Player = new MediaPlayer
            {
                AudioCategory = MediaPlayerAudioCategory.Media,
                AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia
            };
        }

        try
        {
            Player.CommandManager.IsEnabled = true;
            Player.SystemMediaTransportControls.IsEnabled = true;
            Player.PlaybackSession.PlaybackStateChanged += PlaybackStateChanged;
            Player.MediaEnded += MediaEnded;
            Player.MediaFailed += MediaFailed;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal MediaPlayer Player { get; }
    public RadioAudioEngineKind Kind => RadioAudioEngineKind.MediaPlayer;
    public RadioEqualizerPreset Preset => RadioEqualizerPreset.Off;
    public MediaPlaybackState State => Player.PlaybackSession.PlaybackState;
    public TimeSpan Duration => Player.PlaybackSession.NaturalDuration;
    public TimeSpan Position => Player.PlaybackSession.Position;
    public event EventHandler<MediaPlaybackState>? StateChanged;
    public event EventHandler? Completed;
    public event EventHandler? Failed;

    public void Play()
    {
        Player.Play();
    }

    public void Pause()
    {
        Player.Pause();
    }

    public void SetVolume(double volume)
    {
        Player.Volume = Math.Clamp(volume, 0, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Player.PlaybackSession.PlaybackStateChanged -= PlaybackStateChanged;
            Player.MediaEnded -= MediaEnded;
            Player.MediaFailed -= MediaFailed;
            Player.Source = null;
        }
        finally
        {
            try { Player.Dispose(); }
            finally
            {
                _source?.Dispose();
                _source = null;
            }
        }

        GC.SuppressFinalize(this);
    }

    internal void AdoptSource(MediaSource source, MediaPlaybackItem item)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_source is not null)
        {
            throw new InvalidOperationException("A radio engine owns only one source.");
        }

        Player.Source = item;
        _source = source;
    }

    private void PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            StateChanged?.Invoke(this, sender.PlaybackState);
        }
    }

    private void MediaEnded(MediaPlayer sender, object args)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        // Do not propagate native error messages that can contain station URLs.
        if (Volatile.Read(ref _disposed) == 0)
        {
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }
}
