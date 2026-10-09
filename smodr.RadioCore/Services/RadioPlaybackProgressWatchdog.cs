namespace smodr.Services;

/// <summary>Monotonic progress deadline shared by native playback engines.</summary>
public sealed class RadioPlaybackProgressWatchdog
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeSpan _timelineResetThreshold;
    private TimeSpan? _pendingTimelineReset;
    private TimeSpan _position;
    private long _progressAt;

    public RadioPlaybackProgressWatchdog(
        TimeProvider? clock = null,
        TimeSpan? stallTimeout = null,
        TimeSpan? timelineResetThreshold = null)
    {
        _clock = clock ?? TimeProvider.System;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        _timelineResetThreshold = timelineResetThreshold ?? TimeSpan.FromSeconds(5);
        if (_stallTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stallTimeout));
        }

        if (_timelineResetThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timelineResetThreshold));
        }
    }

    public void Reset(TimeSpan position)
    {
        _position = position;
        _pendingTimelineReset = null;
        _progressAt = _clock.GetTimestamp();
    }

    public bool IsStalled(TimeSpan position)
    {
        // Confirm a live timeline rebase with a subsequent small forward sample.
        // This accepts real HLS restarts without letting old/new sample oscillation
        // continually refresh the deadline.
        if (_pendingTimelineReset is { } pending)
        {
            if (position > pending && position - pending < _timelineResetThreshold)
            {
                Reset(position);
            }
            else if (position >= _position)
            {
                _pendingTimelineReset = null;
            }
        }
        else if (position > _position)
        {
            Reset(position);
        }
        else if (_position - position >= _timelineResetThreshold)
        {
            _pendingTimelineReset = position;
        }

        return _clock.GetElapsedTime(_progressAt) >= _stallTimeout;
    }
}
