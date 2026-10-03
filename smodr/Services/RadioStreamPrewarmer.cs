using smodr.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace smodr.Services;

/// <summary>Opt-in silent native source preparation, handed to playback without reopening its URL.</summary>
public sealed class RadioStreamPrewarmer(RadioPlaybackPreferences preferences,
    Func<bool>? canPrefetch = null,
    Func<RadioStation, CancellationToken, Task<PreparedRadioSource?>>? prepare = null,
    TimeProvider? clock = null) : IDisposable
{
    private readonly Func<bool> _canPrefetch = canPrefetch ?? WindowsWarmupPolicy.CanPrefetch;
    private readonly Func<RadioStation, CancellationToken, Task<PreparedRadioSource?>> _prepare = prepare ?? PrepareNativeAsync;
    private readonly RadioStreamWarmupSlot<PreparedRadioSource> _slot = new(clock);
    private CancellationTokenSource? _operation;
    private int _warming;
    private int _disposed;
    private int _version;
    private readonly BackgroundWorkScope _background = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private CancellationTokenSource? _expiry;
    private readonly Lock _gate = new();

    public Task WarmAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Allowed || Interlocked.CompareExchange(ref _warming, 1, 0) != 0)
            {
                return Task.CompletedTask;
            }

            return _background.RunAsync(token => WarmCoreAsync(stations, cancellationToken, token));
        }
    }

    private async Task WarmCoreAsync(IReadOnlyList<RadioStation> stations, CancellationToken cancellationToken,
        CancellationToken lifetime)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        _operation = cancellation;
        var version = Volatile.Read(ref _version);
        try
        {
            var station = stations.Take(8).FirstOrDefault(station =>
                !station.Id.StartsWith("shoutcast:", StringComparison.Ordinal)
                && Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && !uri.IsLoopback);
            if (station is null)
            {
                return;
            }

            cancellation.CancelAfter(TimeSpan.FromSeconds(8));
            var prepared = await _prepare(station, cancellation.Token);
            if (prepared is null)
            {
                return;
            }

            if (!Allowed || cancellation.IsCancellationRequested || version != Volatile.Read(ref _version))
            { prepared.Dispose(); return; }
            _slot.Put(station.StreamUrl, prepared);
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                CancelOne(_expiry);
                var expiry = _expiry = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                _ = _background.RunAsync(_ => ExpireAsync(Interlocked.Increment(ref _version), expiry));
            }
        }
        catch (OperationCanceledException) { /* Auxiliary warmup never blocks or fails playback. */ }
        catch (Exception exception) { AppDiagnostics.Record("stream.prewarm", exception); }
        finally { _operation = null; Volatile.Write(ref _warming, 0); }
    }

    private bool Allowed => Volatile.Read(ref _disposed) == 0 && preferences.Current.PrewarmStreams
        && preferences.Current.Equalizer == RadioEqualizerPreset.Off && _canPrefetch();

    public PreparedRadioSource? Take(RadioStation station)
    {
        Interlocked.Increment(ref _version);
        CancelPending();
        return _slot.Take(station.StreamUrl, Allowed);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _version);
        CancelPending();
        _slot.Clear();
    }

    private void CancelPending()
    {
        CancelOne(_operation);
        CancelOne(_expiry);
    }

    private static void CancelOne(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task ExpireAsync(int version, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), _clock, cancellation.Token);
            if (version == Volatile.Read(ref _version))
            {
                Clear();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppDiagnostics.Record("stream.prewarm-expire", exception); }
        finally { Interlocked.CompareExchange(ref _expiry, null, cancellation); cancellation.Dispose(); }
    }

    private static async Task<PreparedRadioSource?> PrepareNativeAsync(RadioStation station, CancellationToken token)
    {
        var player = new MediaPlayer
        {
            AutoPlay = false,
            Volume = 0,
            AudioCategory = MediaPlayerAudioCategory.Media,
            AudioDeviceType = MediaPlayerAudioDeviceType.Multimedia
        };
        MediaSource? source = null;
        var retained = false;
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Opened(MediaPlayer _, object args) => opened.TrySetResult();
        void Failed(MediaPlayer _, MediaPlayerFailedEventArgs args) => opened.TrySetException(new InvalidOperationException("Prewarm source failed."));
        player.MediaOpened += Opened;
        player.MediaFailed += Failed;
        try
        {
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            source = MediaSource.CreateFromUri(new Uri(station.StreamUrl));
            player.Source = AudioService.CreatePlaybackItem(source, NowPlayingMetadata.ForStation(station));
            await opened.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            retained = true;
            return new PreparedRadioSource(player, source);
        }
        finally
        {
            player.MediaOpened -= Opened;
            player.MediaFailed -= Failed;
            if (!retained)
            {
                player.Source = null;
                try { player.Dispose(); }
                finally { source?.Dispose(); }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Clear();
            _slot.Dispose();
            _background.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    public Task ShutdownAsync() { Dispose(); return _background.StopAsync(); }
}

public sealed class PreparedRadioSource : IDisposable
{
    internal MediaPlayer? Player { get; private set; }
    internal MediaSource? Source { get; private set; }
    internal PreparedRadioSource(MediaPlayer player, MediaSource source) { Player = player; Source = source; }
    internal (MediaPlayer Player, MediaSource Source) Transfer()
    {
        var player = Player ?? throw new InvalidOperationException("Prepared source already consumed.");
        var source = Source!;
        Player = null;
        Source = null;
        return (player, source);
    }
    public void Dispose()
    {
        if (Player is { } player) { player.Source = null; player.Dispose(); Player = null; }
        Source?.Dispose();
        Source = null;
        GC.SuppressFinalize(this);
    }
}
