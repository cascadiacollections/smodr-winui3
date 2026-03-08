using System.Diagnostics;
using smodr.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace smodr.Services;

public class AudioService : IDisposable
{
    private MediaPlayer? _mediaPlayer;
    private Episode? _currentEpisode;
    private bool _isInitialized;

    public event EventHandler<Episode>? EpisodeChanged;
    public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;

    public Episode? CurrentEpisode => _currentEpisode;
    public MediaPlaybackState PlaybackState => _mediaPlayer?.PlaybackSession?.PlaybackState ?? MediaPlaybackState.None;
    public TimeSpan Position => _mediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _mediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;
    public bool IsPlaying => PlaybackState == MediaPlaybackState.Playing;
    public bool IsPaused => PlaybackState == MediaPlaybackState.Paused;

    public void Initialize()
    {
        if (_isInitialized) return;

        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia
        };

        _mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
        _mediaPlayer.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        _mediaPlayer.PlaybackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;

        _isInitialized = true;
    }

    public async Task PlayEpisodeAsync(Episode episode)
    {
        if (!_isInitialized)
            Initialize();

        if (string.IsNullOrEmpty(episode.MediaUrl))
            throw new ArgumentException("Episode has no media URL to play.");

        try
        {
            if (_currentEpisode?.MediaUrl == episode.MediaUrl)
            {
                _mediaPlayer?.Play();
                return;
            }

            _mediaPlayer?.Pause();

            _currentEpisode = episode;
            EpisodeChanged?.Invoke(this, episode);

            var mediaSource = MediaSource.CreateFromUri(new Uri(episode.MediaUrl));

            var displayProperties = mediaSource.CustomProperties;
            displayProperties["Title"] = episode.Title;
            displayProperties["Artist"] = "Kevin Smith & Scott Mosier";
            displayProperties["AlbumTitle"] = "SModcast";

            _mediaPlayer!.Source = mediaSource;
            _mediaPlayer.Play();

            Debug.WriteLine($"Started playing: {episode.Title}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error playing episode: {ex.Message}");
            throw;
        }
    }

    public void Play() => _mediaPlayer?.Play();

    public void Pause() => _mediaPlayer?.Pause();

    public void Stop()
    {
        _mediaPlayer?.Pause();
        if (_mediaPlayer?.PlaybackSession is { } session)
        {
            session.Position = TimeSpan.Zero;
        }
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
        PlaybackStateChanged?.Invoke(this, sender.PlaybackState);
        Debug.WriteLine($"Playback state changed: {sender.PlaybackState}");
    }

    private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args) =>
        PositionChanged?.Invoke(this, sender.Position);

    private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender, object args) =>
        DurationChanged?.Invoke(this, sender.NaturalDuration);

    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
        Debug.WriteLine($"Media failed: {args.Error} - {args.ErrorMessage}");

    private void MediaPlayer_MediaEnded(MediaPlayer sender, object args) =>
        Debug.WriteLine("Media playback ended");

    public void Dispose()
    {
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
            _mediaPlayer.PlaybackSession.PositionChanged -= PlaybackSession_PositionChanged;
            _mediaPlayer.PlaybackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged;
            _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
            _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;

            _mediaPlayer.Pause();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        _isInitialized = false;
        GC.SuppressFinalize(this);
    }
}
