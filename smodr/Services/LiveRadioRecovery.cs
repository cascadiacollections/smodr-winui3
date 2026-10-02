namespace smodr.Services;

/// <summary>
/// Bounds a live stream's buffering and reconnect attempts. It never touches the
/// media engine; callbacks are dispatched by the owner to the player's thread.
/// </summary>
internal sealed class LiveRadioRecovery : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Action<long> _restart;
    private readonly Action<long> _exhausted;
    private readonly Action<long>? _beforeRetry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeSpan _resumeTimeout;
    private readonly TimeSpan _retryBaseDelay;
    private readonly int _maxRetries;
    private CancellationTokenSource? _timer;
    private TimerKind _timerKind;
    private long _epoch;
    private int _attempts;
    private bool _requested;
    private bool _waitingForRetry;
    private bool _disposed;

    private enum TimerKind { None, Stall, Resume, Retry }
    private readonly record struct FailureDecision(long? ExhaustedEpoch, long? RetryEpoch, TimeSpan RetryDelay);

    public LiveRadioRecovery(
        Action<long> restart,
        Action<long> exhausted,
        TimeProvider? clock = null,
        TimeSpan? stallTimeout = null,
        TimeSpan? resumeTimeout = null,
        TimeSpan? retryBaseDelay = null,
        int maxRetries = 3,
        Action<long>? beforeRetry = null)
    {
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(exhausted);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRetries, 10);
        _restart = restart;
        _exhausted = exhausted;
        _beforeRetry = beforeRetry;
        _clock = clock ?? TimeProvider.System;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        _resumeTimeout = resumeTimeout ?? TimeSpan.FromSeconds(2);
        _retryBaseDelay = retryBaseDelay ?? TimeSpan.FromSeconds(2);
        _maxRetries = maxRetries;
    }

    public bool IsRequested
    {
        get { lock (_gate) return _requested; }
    }

    public bool IsCurrent(long epoch)
    {
        lock (_gate) return !_disposed && _requested && _epoch == epoch;
    }

    public bool IsEpoch(long epoch)
    {
        lock (_gate) return !_disposed && _epoch == epoch;
    }

    public void Begin()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _epoch++;
            _requested = true;
            _attempts = 0;
            _waitingForRetry = false;
            ArmLocked(TimerKind.Stall, _stallTimeout);
        }
    }

    public void Buffering()
    {
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry || _timerKind == TimerKind.Stall) return;
            ArmLocked(TimerKind.Stall, _stallTimeout);
        }
    }

    public void Playing()
    {
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry) return;
            // Invalidate a restart already queued onto the player's thread.
            // A recovering native stream can report Playing just as its retry
            // timer fires; that callback must not replace audible playback.
            _epoch++;
            _attempts = 0;
            CancelTimerLocked();
        }
    }

    public void ResumePending()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _epoch++;
            _requested = true;
            _attempts = 0;
            _waitingForRetry = false;
            ArmLocked(TimerKind.Resume, _resumeTimeout);
        }
    }

    public void Fail()
    {
        FailureDecision decision;
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry) return;
            decision = FailLocked();
        }

        ApplyFailureDecision(decision);
    }

    private FailureDecision FailLocked()
    {
        CancelTimerLocked();
        if (_attempts >= _maxRetries)
        {
            _requested = false;
            RuntimeDiagnostics.Counters.Increment(RuntimeCounter.RecoveryExhausted);
            return new(_epoch, null, TimeSpan.Zero);
        }

        _attempts++;
        RuntimeDiagnostics.Counters.Increment(RuntimeCounter.RecoveryRetryScheduled);
        _waitingForRetry = true;
        var delay = TimeSpan.FromTicks(_retryBaseDelay.Ticks * (1L << (_attempts - 1)));
        return new(null, _epoch, delay);
    }

    private void ApplyFailureDecision(FailureDecision decision)
    {
        if (decision.RetryEpoch is { } pendingEpoch)
        {
            // Retire the old stream before the retry clock starts. Otherwise a
            // short delay can elapse first under load and restart out of order.
            try { _beforeRetry?.Invoke(pendingEpoch); }
            finally
            {
                lock (_gate)
                {
                    if (!_disposed && _requested && _waitingForRetry && _epoch == pendingEpoch)
                    {
                        ArmLocked(TimerKind.Retry, decision.RetryDelay);
                    }
                }
            }
        }
        else if (decision.ExhaustedEpoch is { } exhaustedEpoch)
        {
            _exhausted(exhaustedEpoch);
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _epoch++;
            _requested = false;
            _waitingForRetry = false;
            CancelTimerLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _requested = false;
            _epoch++;
            CancelTimerLocked();
        }
    }

    private void ArmLocked(TimerKind kind, TimeSpan delay)
    {
        CancelTimerLocked();
        var cancellation = new CancellationTokenSource();
        _timer = cancellation;
        _timerKind = kind;
        _ = RunTimerAsync(kind, delay, _epoch, cancellation);
    }

    private void CancelTimerLocked()
    {
        _timer?.Cancel();
        _timer = null;
        _timerKind = TimerKind.None;
    }

    private async Task RunTimerAsync(TimerKind kind, TimeSpan delay, long epoch, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delay, _clock, cancellation.Token);
            Action<long>? callback = null;
            FailureDecision? decision = null;
            lock (_gate)
            {
                if (_disposed || !_requested || _epoch != epoch || !ReferenceEquals(_timer, cancellation)) return;
                _timer = null;
                _timerKind = TimerKind.None;
                if (kind == TimerKind.Stall)
                {
                    decision = FailLocked();
                }
                else
                {
                    _waitingForRetry = false;
                    ArmLocked(TimerKind.Stall, _stallTimeout);
                    callback = _restart;
                    RuntimeDiagnostics.Counters.Increment(RuntimeCounter.RecoveryRestartRequested);
                }
            }

            if (decision is { } failure) ApplyFailureDecision(failure);
            callback?.Invoke(epoch);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
