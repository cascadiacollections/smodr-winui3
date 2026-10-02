namespace smodr.Services;

/// <summary>Monotonic progress deadline for native engines without MediaPlayer buffering events.</summary>
public sealed class RadioPlaybackProgressWatchdog(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private TimeSpan _position;
    private long _progressAt;

    public void Reset(TimeSpan position)
    {
        _position = position;
        _progressAt = _clock.GetTimestamp();
    }

    public bool IsStalled(TimeSpan position)
    {
        if (position != _position) Reset(position);
        return _clock.GetElapsedTime(_progressAt) >= TimeSpan.FromSeconds(30);
    }
}
