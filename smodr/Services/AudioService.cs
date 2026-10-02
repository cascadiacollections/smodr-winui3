using System.Diagnostics;
using Microsoft.UI.Dispatching;
using smodr.Models;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace smodr.Services;

public partial class AudioService : IRadioPlayer, IDisposable
{
    private bool _isInitialized;
    private bool _radioEnded;
    private MediaPlayer? _mediaPlayer;
    private MediaSource? _mediaSource;
    private MediaPlaybackItem? _playbackItem;
    private Uri? _currentArtworkUri;
    private readonly List<TimedMetadataTrack> _timedTracks = [];
    private DispatcherQueue? _dispatcher;
    private readonly LiveRadioRecovery _recovery;
    private readonly IcyTrackMonitor _trackMonitor;
    private string? _defaultRenderDeviceId;
    private bool _observingDefaultRenderDevice;
    private readonly RadioPlaybackPreferences? _preferences;
    private readonly RadioStreamPrewarmer? _prewarmer;
    private double _volume = 0.5;
    private long _sourceVersion;
    private long _intentVersion;

    public AudioService(IcyTrackMonitor trackMonitor, RadioPlaybackPreferences? preferences = null,
        RadioStreamPrewarmer? prewarmer = null)
    {
        _preferences = preferences;
        _prewarmer = prewarmer;
        _trackMonitor = trackMonitor;
        _trackMonitor.TrackChanged += TrackMonitor_TrackChanged;
        _trackMonitor.TrackInvalidated += TrackMonitor_TrackInvalidated;
        _recovery = new LiveRadioRecovery(RestartCurrentRadio, ReportRadioFailure,
            beforeRetry: RetireCurrentRadio);
    }

    public RadioStation? CurrentStation { get; private set; }
    public RadioTrackInfo? CurrentTrack { get; private set; }

    public MediaPlaybackState PlaybackState => _dsp is not null || _dspStart is not null
        ? _dspState : _mediaPlayer?.PlaybackSession?.PlaybackState ?? MediaPlaybackState.None;
    public TimeSpan Duration => _dsp?.Duration ?? _mediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;
    public bool IsPlaying => PlaybackState == MediaPlaybackState.Playing;
    public bool IsPaused => PlaybackState == MediaPlaybackState.Paused;
    public bool IsPlaybackRequested => CurrentStation is not null && _recovery.IsRequested;

    public void Dispose()
    {
        if (_observingDefaultRenderDevice)
        {
            try { MediaDevice.DefaultAudioRenderDeviceChanged -= MediaDevice_DefaultAudioRenderDeviceChanged; }
            catch (Exception exception) { AppDiagnostics.Record("station.audio-device-unwatch", exception); }
            _observingDefaultRenderDevice = false;
        }
        _trackMonitor.TrackChanged -= TrackMonitor_TrackChanged;
        _trackMonitor.TrackInvalidated -= TrackMonitor_TrackInvalidated;
        _trackMonitor.Dispose();
        _recovery.Dispose();
        if (_mediaPlayer is not null)
        {
            ReleasePlayer();
        }

        _isInitialized = false;
        GC.SuppressFinalize(this);
    }

    public event EventHandler<RadioStation?>? StationChanged;
    public event EventHandler<RadioTrackUpdate?>? TrackChanged;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler? UserPlaybackStarted;
    public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;

    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        CreatePlayer();
        try
        {
            _defaultRenderDeviceId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        }
        catch (Exception exception) { AppDiagnostics.Record("station.audio-device-current", exception); }
        try
        {
            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;
            _observingDefaultRenderDevice = true;
        }
        catch (Exception exception) { AppDiagnostics.Record("station.audio-device-watch", exception); }
        _isInitialized = true;
    }

    private void CreatePlayer(PreparedRadioSource? prepared = null)
    {
        ReleasePlayer();
        if (prepared is not null)
        {
            (_mediaPlayer, _mediaSource) = prepared.Transfer();
            _mediaPlayer.Volume = _volume;
            _mediaPlayer.CommandManager.IsEnabled = true;
            _mediaPlayer.SystemMediaTransportControls.IsEnabled = true;
            _playbackItem = (MediaPlaybackItem)_mediaPlayer.Source;
            _playbackItem.TimedMetadataTracksChanged += PlaybackItem_TimedMetadataTracksChanged;
            RegisterTimedTracks(_playbackItem);
        }
        else _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia,
            Volume = _volume
        };

        _mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.CommandManager.PlayReceived += CommandManager_PlayReceived;
        _mediaPlayer.CommandManager.PauseReceived += CommandManager_PauseReceived;
    }

    private void ReleasePlayer()
    {
        _sourceVersion++;
        ReleaseDsp();
        _trackMonitor.Stop();
        DetachTimedTracks();
        if (_mediaPlayer is not { } player) return;
        _mediaPlayer = null;
        player.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
        player.MediaFailed -= MediaPlayer_MediaFailed;
        player.MediaEnded -= MediaPlayer_MediaEnded;
        player.CommandManager.PlayReceived -= CommandManager_PlayReceived;
        player.CommandManager.PauseReceived -= CommandManager_PauseReceived;
        player.Source = null;
        player.Dispose();
        _mediaSource?.Dispose();
        _mediaSource = null;
    }

    public Task PlayStationAsync(RadioStation station)
    {
        _intentVersion++;
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
        CurrentStation = station;
        _currentArtworkUri = ParseArtworkUri(station.ArtworkUrl);
        ClearTrack();
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
            ClearTrack();
            StationChanged?.Invoke(this, null);
            throw;
        }

        Debug.WriteLine($"Started playing station: {station.Name}");
        return Task.CompletedTask;
    }

    private void StartRadioSource(RadioStation station, Uri streamUri)
    {
        try { _defaultRenderDeviceId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default); }
        catch (Exception exception) { AppDiagnostics.Record("station.audio-device-current", exception); }
        // A fresh player gives each stream its own event source. Late callbacks
        // from a retired stream cannot be attributed to a different station.
        var preset = _preferences?.Current.Equalizer ?? RadioEqualizerPreset.Off;
        using var prepared = preset == RadioEqualizerPreset.Off ? _prewarmer?.Take(station) : null;
        CreatePlayer(prepared);
        if (preset != RadioEqualizerPreset.Off)
        {
            _ = StartDspAsync(station, streamUri, preset);
            return;
        }
        var metadata = CurrentTrack is { } track
            ? new NowPlayingMetadata(track.Title, track.Artist ?? station.Name, station.Name)
            : NowPlayingMetadata.ForStation(station);
        if (prepared is null) SetPlayerSource(streamUri, metadata);
        SetNowPlayingArtwork(station, _currentArtworkUri);
        _mediaPlayer!.Play();
    }

    private void SetPlayerSource(Uri uri, NowPlayingMetadata metadata)
    {
        var source = MediaSource.CreateFromUri(uri);
        MediaPlaybackItem? item = null;
        try
        {
            item = CreatePlaybackItem(source, metadata);
            if (CurrentStation is not null)
            {
                _playbackItem = item;
                item.TimedMetadataTracksChanged += PlaybackItem_TimedMetadataTracksChanged;
                RegisterTimedTracks(item);
            }
            _mediaPlayer!.Source = item;
            _mediaSource = source;
        }
        catch
        {
            DetachTimedTracks();
            source.Dispose();
            throw;
        }
    }

    private void DetachTimedTracks()
    {
        if (_playbackItem is null) return;
        _playbackItem.TimedMetadataTracksChanged -= PlaybackItem_TimedMetadataTracksChanged;
        foreach (var track in _timedTracks) track.CueEntered -= TimedTrack_CueEntered;
        _timedTracks.Clear();
        _playbackItem = null;
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
        _intentVersion++;
        if (CurrentStation is { } station)
        {
            var preset = _preferences?.Current.Equalizer ?? RadioEqualizerPreset.Off;
            if (_dsp is { } dsp && !_radioEnded && dsp.Preset == preset)
            {
                _recovery.ResumePending();
                dsp.Play();
                PublishDspState(MediaPlaybackState.Playing);
                return;
            }
            if (_radioEnded || _dsp is not null || _mediaPlayer?.Source is null || preset != RadioEqualizerPreset.Off)
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
        _intentVersion++;
        _prewarmer?.Clear();
        _dspStart?.Cancel();
        _dsp?.Pause();
        _trackMonitor.Stop();
        _recovery.Pause();
        if (_dsp is not null || _dspStart is not null) PublishDspState(MediaPlaybackState.Paused);
        _mediaPlayer?.Pause();
        if (CurrentStation is not null)
        {
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Paused);
        }
    }

    public void StopStation()
    {
        _intentVersion++;
        _prewarmer?.Clear();
        if (CurrentStation is null)
        {
            return;
        }

        _recovery.Pause();
        _radioEnded = false;
        ReleasePlayer();

        CurrentStation = null;
        _currentArtworkUri = null;
        ClearTrack();
        StationChanged?.Invoke(this, null);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _dsp?.SetVolume(_volume);
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 1);
        }
    }

    public double GetVolume() => _volume;

    public void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl)
    {
        if (!ReferenceEquals(CurrentStation, station)) return;
        _currentArtworkUri = artworkUrl;
        if (_manualControls is not null) { UpdateManualMetadata(); return; }
        if (_mediaPlayer?.Source is not MediaPlaybackItem item) return;
        try
        {
            var display = item.GetDisplayProperties();
            display.Thumbnail = artworkUrl is { Scheme: "https" or "http" }
                ? RandomAccessStreamReference.CreateFromUri(artworkUrl) : null;
            item.ApplyDisplayProperties(display);
        }
        catch (Exception exception) { AppDiagnostics.Record("artwork.system-media", exception); }
    }

    private static Uri? ParseArtworkUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri : null;

    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        if (_dspStart is not null || _manualControls is not null) return;
        if (!ReferenceEquals(sender, _mediaPlayer?.PlaybackSession)) return;
        if (CurrentStation is not null && !_recovery.IsRequested) return;
        if (CurrentStation is not null)
        {
            switch (sender.PlaybackState)
            {
                case MediaPlaybackState.Buffering:
                    _trackMonitor.Stop();
                    _recovery.Buffering();
                    break;
                case MediaPlaybackState.Playing:
                    _recovery.Playing();
                    if (CurrentStation is { } station) _trackMonitor.Start(station);
                    break;
                default:
                    _trackMonitor.Stop();
                    break;
            }
        }

        PlaybackStateChanged?.Invoke(this, sender.PlaybackState);
        Debug.WriteLine($"Playback state changed: {sender.PlaybackState}");
    }

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
        => RunOnPlayerThread(() => HandleMediaEnded(sender));

    private void HandleMediaEnded(MediaPlayer sender)
    {
        if (!ReferenceEquals(sender, _mediaPlayer)) return;
        if (CurrentStation is not null)
        {
            var loop = FinishedBroadcastPolicy.ShouldLoop(_preferences?.Current.LoopFinishedBroadcasts == true,
                _recovery.IsRequested, Duration);
            var intent = _intentVersion;
            // A finite programme ended normally. Reopen only under explicit opt-in.
            _radioEnded = true;
            _trackMonitor.Stop();
            _recovery.Pause();
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Paused);
            if (loop) RunOnPlayerThread(() =>
            {
                if (intent != _intentVersion || !ReferenceEquals(sender, _mediaPlayer)) return;
                try { Play(); }
                catch (Exception exception) { AppDiagnostics.Record("station.loop", exception); }
            });
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
                try
                {
                    if (ReferenceEquals(sender, _mediaPlayer?.CommandManager) && !IsPlaybackRequested)
                    {
                        Play();
                        UserPlaybackStarted?.Invoke(this, EventArgs.Empty);
                    }
                }
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

    private void MediaDevice_DefaultAudioRenderDeviceChanged(object sender,
        DefaultAudioRenderDeviceChangedEventArgs args)
    {
        var nextDeviceId = args.Id;
        var isDefaultRole = args.Role == AudioDeviceRole.Default;
        var playerAtEvent = _mediaPlayer;
        RunOnPlayerThread(() =>
        {
            if (!_observingDefaultRenderDevice || !ReferenceEquals(playerAtEvent, _mediaPlayer)) return;
            var previous = _defaultRenderDeviceId;
            _defaultRenderDeviceId = nextDeviceId;
            if (AudioOutputChangePolicy.ShouldPause(previous, nextDeviceId,
                isDefaultRole, CurrentStation is not null && _recovery.IsRequested))
            {
                try { Pause(); }
                catch (Exception exception) { AppDiagnostics.Record("station.audio-device-pause", exception); }
            }
        });
    }

    private void TrackMonitor_TrackChanged(object? sender, RadioTrackUpdate update) =>
        RunOnPlayerThread(() => ApplyTrack(update));

    private void TrackMonitor_TrackInvalidated(object? sender, RadioStation station) =>
        RunOnPlayerThread(() => ClearSongForStation(station));

    private void ClearSongForStation(RadioStation station)
    {
        if (!ReferenceEquals(CurrentStation, station) || !_recovery.IsRequested) return;
        ClearTrack();
        UpdateManualMetadata();
        SetNowPlayingArtwork(station, ParseArtworkUri(station.ArtworkUrl));
        if (_mediaPlayer?.Source is not MediaPlaybackItem item) return;
        try
        {
            var metadata = NowPlayingMetadata.ForStation(station);
            var display = item.GetDisplayProperties();
            display.MusicProperties.Title = metadata.Title;
            display.MusicProperties.Artist = metadata.Artist;
            display.MusicProperties.AlbumTitle = metadata.AlbumTitle;
            item.ApplyDisplayProperties(display);
        }
        catch (Exception exception) { AppDiagnostics.Record("track.clear-system-media", exception); }
    }

    private void PlaybackItem_TimedMetadataTracksChanged(MediaPlaybackItem sender,
        IVectorChangedEventArgs args) => RunOnPlayerThread(() =>
    {
        if (ReferenceEquals(sender, _playbackItem)) RegisterTimedTracks(sender);
    });

    private void RegisterTimedTracks(MediaPlaybackItem item)
    {
        const string id3DispatchType = "15260DFFFF49443320FF49443320000F";
        for (var index = 0; index < item.TimedMetadataTracks.Count; index++)
        {
            var track = item.TimedMetadataTracks[index];
            if (!string.Equals(track.DispatchType, id3DispatchType, StringComparison.OrdinalIgnoreCase)
                || _timedTracks.Contains(track)) continue;
            try
            {
                track.CueEntered += TimedTrack_CueEntered;
                item.TimedMetadataTracks.SetPresentationMode((uint)index,
                    TimedMetadataTrackPresentationMode.ApplicationPresented);
                _timedTracks.Add(track);
            }
            catch (Exception exception)
            {
                track.CueEntered -= TimedTrack_CueEntered;
                AppDiagnostics.Record("track.hls-register", exception);
            }
        }
    }

    private void TimedTrack_CueEntered(TimedMetadataTrack sender, MediaCueEventArgs args)
    {
        try
        {
            if (args.Cue is not DataCue { Data: { } data } || data.Length is < 20 or > 65_536)
                return;
            var bytes = new byte[data.Length];
            using var reader = DataReader.FromBuffer(data);
            reader.ReadBytes(bytes);
            RunOnPlayerThread(() =>
            {
                if (!ReferenceEquals(sender.PlaybackItem, _playbackItem)
                    || CurrentStation is not { } station) return;
                var track = HlsId3TrackParser.Parse(bytes, station.Name, out var damaged);
                if (track is not null) ApplyTrack(new RadioTrackUpdate(station, track));
                else if (damaged)
                {
                    AppDiagnostics.Record("track.damaged-hls-cue", new InvalidDataException());
                    ClearSongForStation(station);
                }
            });
        }
        catch (Exception exception) { AppDiagnostics.Record("track.hls-cue", exception); }
    }

    private void ApplyTrack(RadioTrackUpdate update)
    {
        if (!ReferenceEquals(CurrentStation, update.Station) || !_recovery.IsRequested
            || PlaybackState != MediaPlaybackState.Playing || CurrentTrack == update.Track
            || IcyTrackParser.IsAlbumEcho(CurrentTrack, update.Track)) return;
        CurrentTrack = update.Track;
        UpdateManualMetadata();
        // Never pair a new title with the previous song's cover while its
        // catalog lookup is still in flight.
        SetNowPlayingArtwork(update.Station, ParseArtworkUri(update.Station.ArtworkUrl));
        try
        {
            if (_mediaPlayer?.Source is MediaPlaybackItem item)
            {
                var display = item.GetDisplayProperties();
                display.Type = MediaPlaybackType.Music;
                display.MusicProperties.Title = update.Track.Title;
                display.MusicProperties.Artist = update.Track.Artist ?? update.Station.Name;
                display.MusicProperties.AlbumTitle = update.Station.Name;
                item.ApplyDisplayProperties(display);
            }
        }
        catch (Exception exception) { AppDiagnostics.Record("track.system-media", exception); }
        TrackChanged?.Invoke(this, update);
    }

    private void ClearTrack()
    {
        if (CurrentTrack is null) return;
        CurrentTrack = null;
        TrackChanged?.Invoke(this, null);
    }
}
