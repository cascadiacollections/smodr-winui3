using smodr.Models;

namespace smodr.Services;

/// <summary>Runs bounded metadata probes only while the selected stream is playing.</summary>
public sealed class IcyTrackMonitor(ITrackMetadataProbe probe,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _errorInterval = TimeSpan.FromMinutes(1);
    private readonly ITrackMetadataProbe _probe = probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay =
        delay ?? ((duration, token) => Task.Delay(duration, token));
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private RadioStation? _station;
    private long _generation;
    private bool _disposed;

    public event EventHandler<RadioTrackUpdate>? TrackChanged;

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
        }

        CancelPrevious(previous);
        _ = PollAsync(station, uri, generation, current);
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
    }

    private async Task PollAsync(RadioStation station, Uri uri, long generation,
        CancellationTokenSource cancellation)
    {
        RadioTrackInfo? previousTrack = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(7));
                    var result = await _probe.ProbeAsync(uri, timeout.Token).ConfigureAwait(false);
                    if (!result.IsSupported) break;
                    var track = IcyTrackParser.Parse(result.RawMetadata, station.Name);
                    if (track is not null && track != previousTrack)
                    {
                        previousTrack = track;
                        lock (_gate)
                        {
                            if (generation != _generation || _disposed) break;
                        }
                        try { TrackChanged?.Invoke(this, new RadioTrackUpdate(station, track)); }
                        catch (Exception exception) { AppDiagnostics.Record("track.callback", exception); }
                    }
                    await _delay(_pollInterval, cancellation.Token).ConfigureAwait(false);
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
