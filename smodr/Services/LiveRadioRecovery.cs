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
    private readonly TimeSpan _stablePlaybackWindow;
    private long _playingAt;
    private bool _playing;
    private readonly int _maxRetries;
    private readonly RuntimeDiagnosticCounters _diagnostics;
    private CancellationTokenSource? _timer;
    private TimerKind _timerKind;
    private long _epoch;
    private int _attempts;
    private bool _requested;
    private bool _waitingForRetry;
    private bool _reconnecting;
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
        Action<long>? beforeRetry = null, RuntimeDiagnosticCounters? diagnostics = null,
        TimeSpan? stablePlaybackWindow = null)
    {
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(exhausted);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRetries, 10);
        _restart = restart;
        _diagnostics = diagnostics ?? RuntimeDiagnostics.Counters;
        _exhausted = exhausted;
        _beforeRetry = beforeRetry;
        _clock = clock ?? TimeProvider.System;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        _resumeTimeout = resumeTimeout ?? TimeSpan.FromSeconds(2);
        _retryBaseDelay = retryBaseDelay ?? TimeSpan.FromSeconds(2);
        _stablePlaybackWindow = stablePlaybackWindow ?? TimeSpan.FromSeconds(30);
        ValidateDelay(_stallTimeout, nameof(stallTimeout));
        ValidateDelay(_resumeTimeout, nameof(resumeTimeout));
        ValidateDelay(_stablePlaybackWindow, nameof(stablePlaybackWindow));
        ValidateDelay(_retryBaseDelay, nameof(retryBaseDelay), 1L << Math.Max(0, maxRetries - 1));
        _maxRetries = maxRetries;
    }

    public bool IsRequested
    {
        get
        {
            lock (_gate)
            {
                return _requested;
            }
        }
    }

    /// <summary>True from a scheduled retry until audio plays again, the user pauses, or the budget is spent.</summary>
    public bool IsReconnecting
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _requested && _reconnecting;
            }
        }
    }

    public bool IsCurrent(long epoch)
    {
        lock (_gate)
        {
            return !_disposed && _requested && _epoch == epoch;
        }
    }

    public bool IsEpoch(long epoch)
    {
        lock (_gate)
        {
            return !_disposed && _epoch == epoch;
        }
    }

    public void Begin()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _epoch++;
            _requested = true;
            _attempts = 0;
            _playing = false;
            _waitingForRetry = false;
            _reconnecting = false;
            ArmLocked(TimerKind.Stall, _stallTimeout);
        }
    }

    public void Buffering()
    {
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry || _timerKind == TimerKind.Stall)
            {
                return;
            }

            CreditStablePlaybackLocked();
            _playing = false;
            ArmLocked(TimerKind.Stall, _stallTimeout);
        }
    }

    public void Playing()
    {
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry)
            {
                return;
            }

            // Invalidate a restart already queued onto the player's thread.
            // A recovering native stream can report Playing just as its retry
            // timer fires; that callback must not replace audible playback.
            _epoch++;
            // Brief Playing/Buffering flaps must not replenish the retry budget.
            if (!_playing)
            {
                _playingAt = _clock.GetTimestamp();
            }

            _playing = true;
            _reconnecting = false;
            CancelTimerLocked();
        }
    }

    public void ResumePending()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _epoch++;
            _requested = true;
            _attempts = 0;
            _playing = false;
            _waitingForRetry = false;
            _reconnecting = false;
            ArmLocked(TimerKind.Resume, _resumeTimeout);
        }
    }

    public void Fail()
    {
        FailureDecision decision;
        lock (_gate)
        {
            if (_disposed || !_requested || _waitingForRetry)
            {
                return;
            }
            decision = FailLocked();
        }

        ApplyFailureDecision(decision);
    }

    private FailureDecision FailLocked()
    {
        CreditStablePlaybackLocked();
        _playing = false;
        CancelTimerLocked();
        if (_attempts >= _maxRetries)
        {
            _requested = false;
            _reconnecting = false;
            _diagnostics.Increment(RuntimeCounter.RecoveryExhausted);
            return new FailureDecision(_epoch, null, TimeSpan.Zero);
        }

        _attempts++;
        _diagnostics.Increment(RuntimeCounter.RecoveryRetryScheduled);
        _waitingForRetry = true;
        _reconnecting = true;
        var delay = TimeSpan.FromTicks(_retryBaseDelay.Ticks * (1L << (_attempts - 1)));
        return new FailureDecision(null, _epoch, delay);
    }

    private void CreditStablePlaybackLocked()
    {
        if (_playing && _clock.GetElapsedTime(_playingAt) >= _stablePlaybackWindow)
        {
            _attempts = 0;
        }
    }

    private static void ValidateDelay(TimeSpan delay, string parameter, long multiplier = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delay.Ticks, parameter);
        // Task.Delay's supported ceiling, including the largest exponential retry.
        if (delay > TimeSpan.FromMilliseconds(uint.MaxValue - 1) / multiplier)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
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
            if (_disposed)
            {
                return;
            }
            _epoch++;
            _requested = false;
            _waitingForRetry = false;
            _reconnecting = false;
            CancelTimerLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

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
                if (_disposed || !_requested || _epoch != epoch || !ReferenceEquals(_timer, cancellation))
                {
                    return;
                }

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
                    _diagnostics.Increment(RuntimeCounter.RecoveryRestartRequested);
                }
            }

            if (decision is { } failure)
            {
                ApplyFailureDecision(failure);
            }

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
