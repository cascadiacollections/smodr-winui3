namespace smodr.Services;

/// <summary>Monotonic progress deadline shared by native playback engines.</summary>
public sealed class RadioPlaybackProgressWatchdog
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stallTimeout;
    private TimeSpan _position;
    private long _progressAt;

    public RadioPlaybackProgressWatchdog(TimeProvider? clock = null, TimeSpan? stallTimeout = null)
    {
        _clock = clock ?? TimeProvider.System;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        if (_stallTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stallTimeout));
    }

    public void Reset(TimeSpan position)
    {
        _position = position;
        _progressAt = _clock.GetTimestamp();
    }

    public bool IsStalled(TimeSpan position)
    {
        // A retired/native session can briefly report an older position. Only
        // forward progress earns a new deadline; the owner resets on each source.
        if (position > _position) Reset(position);
        return _clock.GetElapsedTime(_progressAt) >= _stallTimeout;
    }
}
