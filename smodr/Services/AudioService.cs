using System.Diagnostics;
using Microsoft.UI.Dispatching;
using smodr.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace smodr.Services;

public class AudioService : IRadioPlayer, IDisposable
{
    private bool _isInitialized;
    private bool _radioEnded;
    private MediaPlayer? _mediaPlayer;
    private MediaSource? _mediaSource;
    private DispatcherQueue? _dispatcher;
    private readonly LiveRadioRecovery _recovery;

    public AudioService()
    {
        _recovery = new LiveRadioRecovery(RestartCurrentRadio, ReportRadioFailure,
            beforeRetry: RetireCurrentRadio);
    }

    public Episode? CurrentEpisode { get; private set; }
    public RadioStation? CurrentStation { get; private set; }

    public MediaPlaybackState PlaybackState => _mediaPlayer?.PlaybackSession?.PlaybackState ?? MediaPlaybackState.None;
    public TimeSpan Position => _mediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _mediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;
    public bool IsPlaying => PlaybackState == MediaPlaybackState.Playing;
    public bool IsPaused => PlaybackState == MediaPlaybackState.Paused;
    public bool IsPlaybackRequested => CurrentStation is not null && _recovery.IsRequested;

    public void Dispose()
    {
        _recovery.Dispose();
        if (_mediaPlayer is not null)
        {
            ReleasePlayer();
        }

        _isInitialized = false;
        GC.SuppressFinalize(this);
    }

    public event EventHandler<Episode>? EpisodeChanged;
    public event EventHandler<RadioStation?>? StationChanged;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        CreatePlayer();
        _isInitialized = true;
    }

    private void CreatePlayer()
    {
        var volume = _mediaPlayer?.Volume ?? 0.5;
        ReleasePlayer();
        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia,
            Volume = volume
        };

        _mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
        _mediaPlayer.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        _mediaPlayer.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.CommandManager.PlayReceived += CommandManager_PlayReceived;
        _mediaPlayer.CommandManager.PauseReceived += CommandManager_PauseReceived;
    }

    private void ReleasePlayer()
    {
        if (_mediaPlayer is not { } player) return;
        _mediaPlayer = null;
        player.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
        player.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
        player.PlaybackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged;
        player.MediaFailed -= MediaPlayer_MediaFailed;
        player.MediaEnded -= MediaPlayer_MediaEnded;
        player.CommandManager.PlayReceived -= CommandManager_PlayReceived;
        player.CommandManager.PauseReceived -= CommandManager_PauseReceived;
        player.Source = null;
        player.Dispose();
        _mediaSource?.Dispose();
        _mediaSource = null;
    }

    public Task PlayEpisodeAsync(Episode episode)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        if (string.IsNullOrEmpty(episode.MediaUrl))
        {
            throw new ArgumentException("Episode has no media URL to play.");
        }

        try
        {
            _recovery.Pause();
            _radioEnded = false;
            if (_mediaPlayer is not null
                && string.Equals(CurrentEpisode?.MediaUrl, episode.MediaUrl, StringComparison.Ordinal))
            {
                _mediaPlayer?.Play();
                return Task.CompletedTask;
            }

            CreatePlayer();

            CurrentEpisode = episode;
            CurrentStation = null;
            EpisodeChanged?.Invoke(this, episode);

            SetPlayerSource(new Uri(episode.MediaUrl), NowPlayingMetadata.ForEpisode(episode));
            _mediaPlayer!.Play();

            Debug.WriteLine($"Started playing: {episode.Title}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error playing episode: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task PlayStationAsync(RadioStation station)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
            || streamUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Station has no valid stream URL to play.", nameof(station));
        }

        ReleasePlayer();
        _recovery.Begin();
        _radioEnded = false;
        CurrentEpisode = null;
        CurrentStation = station;
        StationChanged?.Invoke(this, station);
        try
        {
            StartRadioSource(station, streamUri);
        }
        catch
        {
            _recovery.Pause();
            ReleasePlayer();
            CurrentStation = null;
            StationChanged?.Invoke(this, null);
            throw;
        }

        Debug.WriteLine($"Started playing station: {station.Name}");
        return Task.CompletedTask;
    }

    private void StartRadioSource(RadioStation station, Uri streamUri)
    {
        // A fresh player gives each stream its own event source. Late callbacks
        // from a retired stream cannot be attributed to a different station.
        CreatePlayer();
        SetPlayerSource(streamUri, NowPlayingMetadata.ForStation(station));
        _mediaPlayer!.Play();
    }

    private void SetPlayerSource(Uri uri, NowPlayingMetadata metadata)
    {
        var source = MediaSource.CreateFromUri(uri);
        MediaPlaybackItem? item = null;
        try
        {
            item = CreatePlaybackItem(source, metadata);
            _mediaPlayer!.Source = item;
            _mediaSource = source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    internal static MediaPlaybackItem CreatePlaybackItem(MediaSource mediaSource, NowPlayingMetadata metadata)
    {
        var item = new MediaPlaybackItem(mediaSource);
        var display = item.GetDisplayProperties();
        display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = metadata.Title;
        display.MusicProperties.Artist = metadata.Artist;
        display.MusicProperties.AlbumTitle = metadata.AlbumTitle;
        item.ApplyDisplayProperties(display);
        return item;
    }

    public void Play()
    {
        if (CurrentStation is { } station)
        {
            if (_radioEnded || _mediaPlayer?.Source is null)
            {
                _recovery.Begin();
                _radioEnded = false;
                try
                {
                    StartRadioSource(station, new Uri(station.StreamUrl));
                }
                catch
                {
                    _recovery.Pause();
                    throw;
                }
                return;
            }

            _recovery.ResumePending();
        }

        _mediaPlayer?.Play();
    }

    public void Pause()
    {
        _recovery.Pause();
        _mediaPlayer?.Pause();
        if (CurrentStation is not null)
        {
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Paused);
        }
    }

    public void Stop()
    {
        _mediaPlayer?.Pause();
        if (_mediaPlayer?.PlaybackSession is { } session)
        {
            session.Position = TimeSpan.Zero;
        }
    }

    public void StopStation()
    {
        if (CurrentStation is null)
        {
            return;
        }

        _recovery.Pause();
        _radioEnded = false;
        ReleasePlayer();

        CurrentStation = null;
        StationChanged?.Invoke(this, null);
    }

    public void SetPosition(TimeSpan position)
    {
        if (_mediaPlayer?.PlaybackSession is { } session)
        {
            session.Position = position;
        }
    }

    public void SetVolume(double volume)
    {
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 1);
        }
    }

    public double GetVolume() => _mediaPlayer?.Volume ?? 0.5;

    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (!ReferenceEquals(sender, _mediaPlayer?.PlaybackSession)) return;
        if (CurrentStation is not null && !_recovery.IsRequested) return;
        if (CurrentStation is not null)
        {
            switch (sender.PlaybackState)
            {
                case MediaPlaybackState.Buffering:
                    _recovery.Buffering();
                    break;
                case MediaPlaybackState.Playing:
                    _recovery.Playing();
                    break;
            }
        }

        PlaybackStateChanged?.Invoke(this, sender.PlaybackState);
        Debug.WriteLine($"Playback state changed: {sender.PlaybackState}");
    }

    private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args) =>
        PositionChanged?.Invoke(this, sender.Position);

    private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender, object args) =>
        DurationChanged?.Invoke(this, sender.NaturalDuration);

    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (!ReferenceEquals(sender, _mediaPlayer)) return;
        var message = string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? args.Error.ToString()
            : args.ErrorMessage;
        Debug.WriteLine($"Media failed: {args.Error} - {message}");
        AppDiagnostics.Record("station.media-failed", new InvalidOperationException(args.Error.ToString()));
        if (CurrentStation is not null && _recovery.IsRequested)
        {
            _recovery.Fail();
            if (_recovery.IsRequested)
            {
                PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Buffering);
            }
            return;
        }

        if (CurrentStation is null)
        {
            PlaybackFailed?.Invoke(this, "This stream could not be played. Try another station.");
        }
    }

    private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        if (!ReferenceEquals(sender, _mediaPlayer)) return;
        if (CurrentStation is not null)
        {
            // A finite programme ended normally. Do not reconnect and loop it.
            _radioEnded = true;
            _recovery.Pause();
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Paused);
        }
        Debug.WriteLine("Media playback ended");
    }

    private void CommandManager_PlayReceived(MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerPlayReceivedEventArgs args)
    {
        if (!ReferenceEquals(sender, _mediaPlayer?.CommandManager) || CurrentStation is null) return;
        var deferral = args.GetDeferral();
        args.Handled = true;
        if (_dispatcher?.TryEnqueue(() =>
            {
                try { if (ReferenceEquals(sender, _mediaPlayer?.CommandManager)) Play(); }
                catch (Exception exception) { AppDiagnostics.Record("station.system-play", exception); }
                finally { deferral.Complete(); }
            }) != true) deferral.Complete();
    }

    private void CommandManager_PauseReceived(MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerPauseReceivedEventArgs args)
    {
        if (!ReferenceEquals(sender, _mediaPlayer?.CommandManager) || CurrentStation is null) return;
        var deferral = args.GetDeferral();
        args.Handled = true;
        if (_dispatcher?.TryEnqueue(() =>
            {
                try { if (ReferenceEquals(sender, _mediaPlayer?.CommandManager)) Pause(); }
                catch (Exception exception) { AppDiagnostics.Record("station.system-pause", exception); }
                finally { deferral.Complete(); }
            }) != true) deferral.Complete();
    }

    private void RestartCurrentRadio(long epoch) => RunOnPlayerThread(() =>
    {
        if (!_recovery.IsCurrent(epoch) || CurrentStation is not { } station) return;
        try
        {
            StartRadioSource(station, new Uri(station.StreamUrl));
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Buffering);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("station.reconnect", exception);
            _recovery.Fail();
        }
    });

    private void RetireCurrentRadio(long epoch) => RunOnPlayerThread(() =>
    {
        if (_recovery.IsCurrent(epoch) && CurrentStation is not null) ReleasePlayer();
    });

    private void ReportRadioFailure(long epoch) => RunOnPlayerThread(() =>
    {
        if (!_recovery.IsEpoch(epoch) || CurrentStation is null) return;
        ReleasePlayer();
        PlaybackFailed?.Invoke(this, "The stream stopped responding. Select Play to retry.");
    });

    private void RunOnPlayerThread(Action action)
    {
        // Always enqueue: MediaFailed can run on this same thread, and tearing
        // down its MediaPlayer inside the native callback is unsafe.
        _dispatcher?.TryEnqueue(() => action());
    }
}
