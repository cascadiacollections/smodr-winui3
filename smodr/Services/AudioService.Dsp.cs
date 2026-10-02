using Microsoft.UI.Xaml;
using smodr.Models;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace smodr.Services;

public partial class AudioService
{
    private RadioDspSession? _dsp;
    private CancellationTokenSource? _dspStart;
    private SystemMediaTransportControls? _manualControls;
    private MediaPlaybackState _dspState = MediaPlaybackState.None;
    private DispatcherTimer? _dspWatchdog;
    private readonly RadioPlaybackProgressWatchdog _dspProgress = new();

    private async Task StartDspAsync(RadioStation station, Uri uri, RadioEqualizerPreset preset)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _dspStart = cancellation;
        var version = _sourceVersion;
        RadioDspSession? pending = null;
        try
        {
            PublishDspState(MediaPlaybackState.Opening);
            pending = await RadioDspSession.CreateAsync(uri, preset, cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _sourceVersion
                || !ReferenceEquals(station, CurrentStation) || !_recovery.IsRequested) return;
            _dsp = pending;
            pending = null;
            // A source-less player supplies SMTC only. It never opens or decodes this stream.
            _mediaPlayer!.CommandManager.IsEnabled = false;
            _manualControls = _mediaPlayer.SystemMediaTransportControls;
            _manualControls.IsEnabled = true;
            _manualControls.IsPlayEnabled = true;
            _manualControls.IsPauseEnabled = true;
            _manualControls.ButtonPressed += ManualControls_ButtonPressed;
            _dsp.Completed += Dsp_Completed;
            _dsp.Failed += Dsp_Failed;
            UpdateManualMetadata();
            _dsp.SetVolume(_volume);
            _dsp.Play();
            StartDspWatchdog();
            PublishDspState(MediaPlaybackState.Playing);
        }
        catch (OperationCanceledException) when (version != _sourceVersion || !_recovery.IsRequested) { }
        catch (Exception exception)
        {
            if (version != _sourceVersion || !ReferenceEquals(station, CurrentStation) || !_recovery.IsRequested) return;
            AppDiagnostics.Record("station.dsp-start", exception);
            PublishDspState(MediaPlaybackState.Buffering);
            _recovery.Fail();
        }
        finally
        {
            pending?.Dispose();
            if (ReferenceEquals(_dspStart, cancellation)) _dspStart = null;
        }
    }

    private void ReleaseDsp()
    {
        var pending = _dspStart;
        _dspStart = null;
        pending?.Cancel(); // Async creator owns disposal until its continuation finishes.
        if (_dspWatchdog is { } timer)
        {
            timer.Stop();
            timer.Tick -= DspWatchdog_Tick;
            _dspWatchdog = null;
        }
        if (_manualControls is { } controls)
        {
            controls.ButtonPressed -= ManualControls_ButtonPressed;
            controls.IsEnabled = false;
            _manualControls = null;
        }
        if (_dsp is { } dsp)
        {
            _dsp = null;
            dsp.Completed -= Dsp_Completed;
            dsp.Failed -= Dsp_Failed;
            dsp.Dispose();
        }
        _dspState = MediaPlaybackState.None;
    }

    private void PublishDspState(MediaPlaybackState state)
    {
        _dspState = state;
        if (_manualControls is { } controls)
            controls.PlaybackStatus = state switch
            {
                MediaPlaybackState.Playing => MediaPlaybackStatus.Playing,
                MediaPlaybackState.Paused => MediaPlaybackStatus.Paused,
                MediaPlaybackState.Opening or MediaPlaybackState.Buffering => MediaPlaybackStatus.Changing,
                _ => MediaPlaybackStatus.Stopped
            };
        if (state == MediaPlaybackState.Playing)
        {
            _dspWatchdog?.Start();
            _recovery.Playing();
            if (CurrentStation is { } station) _trackMonitor.Start(station);
            _dspProgress.Reset(_dsp?.Position ?? TimeSpan.Zero);
        }
        else
        {
            _dspWatchdog?.Stop();
            _trackMonitor.Stop();
        }
        PlaybackStateChanged?.Invoke(this, state);
    }

    private void StartDspWatchdog()
    {
        _dspWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _dspWatchdog.Tick += DspWatchdog_Tick;
    }

    private void DspWatchdog_Tick(object? sender, object args)
    {
        if (_dsp is not { } dsp || !_recovery.IsRequested || _dspState != MediaPlaybackState.Playing) return;
        try
        {
            if (_dspProgress.IsStalled(dsp.Position))
            {
                PublishDspState(MediaPlaybackState.Buffering);
                _recovery.Fail();
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("station.dsp-progress", exception);
            PublishDspState(MediaPlaybackState.Buffering);
            _recovery.Fail();
        }
    }

    private void Dsp_Failed(object? sender, EventArgs args) => RunOnPlayerThread(() =>
    {
        if (!ReferenceEquals(sender, _dsp) || !_recovery.IsRequested) return;
        AppDiagnostics.Record("station.dsp-failed", new InvalidOperationException());
        PublishDspState(MediaPlaybackState.Buffering);
        _recovery.Fail();
    });

    private void Dsp_Completed(object? sender, EventArgs args) => RunOnPlayerThread(() =>
    {
        if (!ReferenceEquals(sender, _dsp) || !_recovery.IsRequested) return;
        var loop = FinishedBroadcastPolicy.ShouldLoop(_preferences?.Current.LoopFinishedBroadcasts == true, true, Duration);
        var intent = _intentVersion;
        _radioEnded = true;
        _dsp!.Pause();
        _recovery.Pause();
        PublishDspState(MediaPlaybackState.Paused);
        if (loop) RunOnPlayerThread(() =>
        {
            if (intent != _intentVersion || !ReferenceEquals(sender, _dsp)) return;
            try { Play(); }
            catch (Exception exception) { AppDiagnostics.Record("station.dsp-loop", exception); }
        });
    });

    private void ManualControls_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args) => RunOnPlayerThread(() =>
    {
        if (!ReferenceEquals(sender, _manualControls) || CurrentStation is null) return;
        try
        {
            if (args.Button == SystemMediaTransportControlsButton.Play && !_recovery.IsRequested)
            {
                Play();
                UserPlaybackStarted?.Invoke(this, EventArgs.Empty);
            }
            else if (args.Button == SystemMediaTransportControlsButton.Pause) Pause();
        }
        catch (Exception exception) { AppDiagnostics.Record("station.dsp-system-control", exception); }
    });

    private void UpdateManualMetadata()
    {
        if (_manualControls is not { } controls || CurrentStation is not { } station) return;
        try
        {
            var metadata = CurrentTrack is { } track
                ? new NowPlayingMetadata(track.Title, track.Artist ?? station.Name, station.Name)
                : NowPlayingMetadata.ForStation(station);
            var display = controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = metadata.Title;
            display.MusicProperties.Artist = metadata.Artist;
            display.MusicProperties.AlbumTitle = metadata.AlbumTitle;
            display.Thumbnail = _currentArtworkUri is { Scheme: "http" or "https" } artwork
                ? RandomAccessStreamReference.CreateFromUri(artwork) : null;
            display.Update();
        }
        catch (Exception exception) { AppDiagnostics.Record("station.dsp-system-metadata", exception); }
    }
}
