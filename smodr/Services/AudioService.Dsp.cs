using Microsoft.UI.Xaml;
using smodr.Models;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace smodr.Services;

public partial class AudioService
{
    private AudioGraphRadioEngine? _dsp;
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
        AudioGraphRadioEngine? pending = null;
        try
        {
            PublishDspState(MediaPlaybackState.Opening);
            pending = await AudioGraphRadioEngine.CreateAsync(uri, preset, cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _sourceVersion
                || !ReferenceEquals(station, CurrentStation) || !_recovery.IsRequested)
            {
                return;
            }

            _dsp = pending;
            pending = null;
            // A source-less player supplies SMTC only. It never opens or decodes this stream.
            if (SystemPlayer!.Source is not null)
            {
                throw new InvalidOperationException("DSP system-control bridge must have no source.");
            }

            SystemPlayer.CommandManager.IsEnabled = false;
            _manualControls = SystemPlayer.SystemMediaTransportControls;
            _manualControls.IsEnabled = true;
            _manualControls.IsPlayEnabled = true;
            _manualControls.IsPauseEnabled = true;
            _manualControls.ButtonPressed += ManualControls_ButtonPressed;
            // Preserve the source-less MediaPlayer solely as the manual SMTC bridge.
            _engines.Replace(_dsp, disposePrevious: false);
            UpdateManualMetadata();
            StartDspWatchdog();
            _engines.Play();
        }
        catch (OperationCanceledException) when (version != _sourceVersion || !_recovery.IsRequested) { }
        catch (Exception exception)
        {
            if (version != _sourceVersion || !ReferenceEquals(station, CurrentStation) || !_recovery.IsRequested)
            {
                return;
            }

            AppDiagnostics.Record("station.dsp-start", exception);
            PublishDspState(MediaPlaybackState.Buffering);
            _recovery.Fail();
        }
        finally
        {
            pending?.Dispose();
            if (ReferenceEquals(_dspStart, cancellation))
            {
                _dspStart = null;
            }
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
            if (ReferenceEquals(_engines.Current, dsp))
            {
                _engines.Replace(null);
            }
            else
            {
                dsp.Dispose();
            }
        }
        _dspState = MediaPlaybackState.None;
    }

    private void PublishDspState(MediaPlaybackState state)
    {
        _dspState = state;
        if (_manualControls is { } controls)
        {
            controls.PlaybackStatus = state switch
            {
                MediaPlaybackState.Playing => MediaPlaybackStatus.Playing,
                MediaPlaybackState.Paused => MediaPlaybackStatus.Paused,
                MediaPlaybackState.Opening or MediaPlaybackState.Buffering => MediaPlaybackStatus.Changing,
                _ => MediaPlaybackStatus.Stopped
            };
        }

        if (state == MediaPlaybackState.Playing)
        {
            _dspWatchdog?.Start();
            _recovery.Playing();
            if (CurrentStation is { } station)
            {
                _trackMonitor.Start(station);
            }

            _dspProgress.Reset(_dsp?.Position ?? TimeSpan.Zero);
        }
        else
        {
            _dspWatchdog?.Stop();
            _trackMonitor.Stop();
            if (state is MediaPlaybackState.Opening or MediaPlaybackState.Buffering)
            {
                _recovery.Buffering();
            }
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
        if (_dsp is not { } dsp || !_recovery.IsRequested || _dspState != MediaPlaybackState.Playing)
        {
            return;
        }

        try
        {
            if (!_dspProgress.IsStalled(dsp.Position))
            {
                return;
            }

            PublishDspState(MediaPlaybackState.Buffering);
            _recovery.Fail();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("station.dsp-progress", exception);
            PublishDspState(MediaPlaybackState.Buffering);
            _recovery.Fail();
        }
    }

    private void ManualControls_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        RunOnPlayerThread(() =>
        {
            if (!ReferenceEquals(sender, _manualControls) || CurrentStation is null)
            {
                return;
            }

            try
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play when !_recovery.IsRequested:
                        Play();
                        UserPlaybackStarted?.Invoke(this, EventArgs.Empty);
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        Pause();
                        break;
                }
            }
            catch (Exception exception) { AppDiagnostics.Record("station.dsp-system-control", exception); }
        });
    }

    private void UpdateManualMetadata()
    {
        if (_manualControls is not { } controls || CurrentStation is not { } station)
        {
            return;
        }

        try
        {
            var metadata = NowPlayingMetadata.ForPlayback(station, CurrentTrack);
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
