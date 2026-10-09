namespace smodr.Services;

/// <summary>A one-shot wall-clock timer independent of the current station.</summary>
public sealed class PlaybackSleepTimer : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private DateTimeOffset? _endsAt;
    private long _generation;

    public PlaybackSleepTimer(TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _clock, token));
    }

    public DateTimeOffset? EndsAt
    {
        get
        {
            lock (_gate)
            {
                return _endsAt;
            }
        }
    }

    public TimeSpan? Remaining
    {
        get
        {
            lock (_gate)
            {
                return _endsAt is { } end ? TimeSpan.FromTicks(Math.Max(0, (end - _clock.GetUtcNow()).Ticks)) : null;
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            previous = _cancellation;
            _cancellation = null;
            _endsAt = null;
            ++_generation;
        }

        CancelPrevious(previous);
    }

    public event EventHandler? Elapsed;

    public void Start(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _cancellation;
            current = new CancellationTokenSource();
            _cancellation = current;
            _endsAt = _clock.GetUtcNow() + duration;
            generation = ++_generation;
        }

        CancelPrevious(previous);
        _ = RunAsync(duration, generation, current);
    }

    public void Cancel()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _cancellation;
            _cancellation = null;
            _endsAt = null;
            ++_generation;
        }

        CancelPrevious(previous);
    }

    private async Task RunAsync(TimeSpan duration, long generation, CancellationTokenSource cancellation)
    {
        var elapsed = false;
        try
        {
            await _delay(duration, cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposed && !cancellation.IsCancellationRequested && generation == _generation)
                {
                    _cancellation = null;
                    _endsAt = null;
                    elapsed = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("sleep-timer.delay", exception);
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _cancellation = null;
                    _endsAt = null;
                }
            }
        }
        finally
        {
            cancellation.Dispose();
        }

        if (elapsed)
        {
            try { Elapsed?.Invoke(this, EventArgs.Empty); }
            catch (Exception exception) { AppDiagnostics.Record("sleep-timer.elapsed", exception); }
        }
    }

    private static void CancelPrevious(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException)
        {
            /* The previous timer just completed. */
        }
    }
}
