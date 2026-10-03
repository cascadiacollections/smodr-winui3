using smodr.Models;

namespace smodr.Services;

/// <summary>Tracks ICY metadata only while the selected stream is playing.</summary>
public sealed class IcyTrackMonitor(ITrackMetadataProbe probe,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeProvider? clock = null,
    IContinuousTrackMetadataReader? continuousReader = null,
    RuntimeDiagnosticCounters? diagnostics = null) : IDisposable
{
    private readonly RuntimeDiagnosticCounters _diagnostics = diagnostics ?? RuntimeDiagnostics.Counters;
    // Windows MediaPlayer does not surface ICY titles. Prefer a continuous
    // sidecar connection; bounded probes remain a fallback if it drops.
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _errorInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _continuousRetryInterval = TimeSpan.FromMinutes(1);
    private readonly ITrackMetadataProbe _probe = probe;
    private readonly IContinuousTrackMetadataReader? _continuousReader = continuousReader;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((duration, token) => Task.Delay(duration, token));
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private RadioStation? _station;
    private long _generation;
    private bool _disposed;
    private readonly BackgroundWorkScope _background = new();

    public event EventHandler<RadioTrackUpdate>? TrackChanged;
    public event EventHandler<RadioStation>? TrackInvalidated;

    public void Start(RadioStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")) return;
        // An HLS playlist is not an ICY audio response. Its timed metadata is
        // handled by the native playback item's ID3 cue track when available.
        if (uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return;

        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancellation is not null && ReferenceEquals(_station, station)) return;
            previous = _cancellation;
            current = new CancellationTokenSource();
            _cancellation = current;
            _station = station;
            generation = ++_generation;
            // Even already-canceled work must enter PollAsync's finally to release its owner CTS.
            _ = _background.RunAsync(_ => Task.Run(() => PollAsync(station, uri, generation, current), CancellationToken.None));
        }

        CancelPrevious(previous);
    }

    public void Stop()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed) return;
            previous = _cancellation;
            _cancellation = null;
            _station = null;
            ++_generation;
        }
        CancelPrevious(previous);
    }

    public Task ShutdownAsync() { Dispose(); return _background.StopAsync(); }

    public void Dispose()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            previous = _cancellation;
            _cancellation = null;
            _station = null;
            ++_generation;
        }
        CancelPrevious(previous);
        _background.Dispose();
    }

    private async Task PollAsync(RadioStation station, Uri uri, long generation,
        CancellationTokenSource cancellation)
    {
        RadioTrackInfo? previousTrack = null;
        var previousCueDamaged = false;
        var nextContinuousAttempt = DateTimeOffset.MinValue;
        void Publish(string raw)
        {
            lock (_gate)
            {
                if (generation != _generation || _disposed)
                {
                    _diagnostics.Increment(RuntimeCounter.MetadataRetired);
                    return;
                }
            }
            var track = IcyTrackParser.Parse(raw, station.Name);
            if (track is null && IcyTrackParser.IsDamagedSongCue(raw, station.Name))
            {
                lock (_gate)
                {
                    if (generation != _generation || _disposed || previousCueDamaged) return;
                    previousCueDamaged = true;
                    previousTrack = null;
                }
                _diagnostics.Increment(RuntimeCounter.MetadataRejectedDamaged);
                try { TrackInvalidated?.Invoke(this, station); }
                catch (Exception exception) { AppDiagnostics.Record("track.callback", exception); }
                return;
            }
            if (track is null)
            {
                _diagnostics.Increment(string.IsNullOrWhiteSpace(raw) ? RuntimeCounter.MetadataRejectedEmpty
                    : raw.Length > 4096 ? RuntimeCounter.MetadataRejectedOversize : RuntimeCounter.MetadataRejectedNonSong);
                return;
            }
            lock (_gate)
            {
                if (generation != _generation || _disposed) return;
                if (track == previousTrack)
                {
                    _diagnostics.Increment(RuntimeCounter.MetadataDuplicate);
                    return;
                }
                previousTrack = track;
                previousCueDamaged = false;
            }
            _diagnostics.Increment(RuntimeCounter.MetadataAccepted);
            try { TrackChanged?.Invoke(this, new RadioTrackUpdate(station, track)); }
            catch (Exception exception) { AppDiagnostics.Record("track.callback", exception); }
        }
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (_continuousReader is not null && _clock.GetUtcNow() >= nextContinuousAttempt)
                {
                    try
                    {
                        if (!await _continuousReader.ListenAsync(uri, Publish, cancellation.Token)
                            .ConfigureAwait(false)) break;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                    catch (Exception exception) { AppDiagnostics.Record("track.continuous", exception); }
                    nextContinuousAttempt = _clock.GetUtcNow() + _continuousRetryInterval;
                }
                try
                {
                    var startedAt = _clock.GetTimestamp();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(35));
                    var result = await _probe.ProbeAsync(uri, timeout.Token).ConfigureAwait(false);
                    if (!result.IsSupported) break;
                    if (result.RawMetadata is { } raw) Publish(raw);
                    var remaining = _pollInterval - _clock.GetElapsedTime(startedAt);
                    if (remaining > TimeSpan.Zero)
                        await _delay(remaining, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Metadata is auxiliary. Even an unexpected probe failure must
                    // never escape this fire-and-forget loop or affect playback.
                    AppDiagnostics.Record("track.probe", exception);
                    await _delay(_errorInterval, cancellation.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _cancellation = null;
                    _station = null;
                }
            }
            cancellation.Dispose();
        }
    }

    private static void CancelPrevious(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* The prior probe just completed. */ }
    }
}
