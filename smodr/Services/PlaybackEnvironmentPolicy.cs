namespace smodr.Services;

internal enum PlaybackEnvironmentAction { None, Hold, Resume }

/// <summary>Owner-thread policy. OS interruption is not a user pause, and never grants new playback intent.</summary>
internal sealed class PlaybackEnvironmentPolicy
{
    private bool _sleepInterrupted;
    private bool _networkInterrupted;
    public bool IsBlocked { get; private set; }
    public bool IsResumePending { get; private set; }

    public void UserIntent(bool requested)
    {
        IsResumePending = requested && IsBlocked;
        if (requested)
        {
            return;
        }

        _sleepInterrupted = false; _networkInterrupted = false;
    }

    public PlaybackEnvironmentAction Update(bool suspended, bool connected, bool requested,
        bool resumeAfterSleep, bool resumeAfterNetwork)
    {
        var blocked = suspended || !connected;
        if (blocked)
        {
            if (suspended)
            {
                _sleepInterrupted = true;
            }

            if (!connected)
            {
                _networkInterrupted = true;
            }

            if (IsBlocked)
            {
                return PlaybackEnvironmentAction.None;
            }

            IsBlocked = true;
            IsResumePending = requested;
            return PlaybackEnvironmentAction.Hold;
        }
        if (!IsBlocked)
        {
            return PlaybackEnvironmentAction.None;
        }

        IsBlocked = false;
        var resume = IsResumePending && (!_sleepInterrupted || resumeAfterSleep)
            && (!_networkInterrupted || resumeAfterNetwork);
        IsResumePending = false;
        _sleepInterrupted = false;
        _networkInterrupted = false;
        return resume ? PlaybackEnvironmentAction.Resume : PlaybackEnvironmentAction.None;
    }
}
