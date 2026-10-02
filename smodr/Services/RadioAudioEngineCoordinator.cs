using Windows.Media.Playback;

namespace smodr.Services;

/// <summary>UI-thread ownership and controls; callbacks from native threads are generation-checked again on delivery.</summary>
internal sealed class RadioAudioEngineCoordinator(Action<Action> dispatch) : IDisposable
{
    private IRadioAudioEngine? _current;
    private long _generation;
    private double _volume = 0.5;
    private bool _disposed;
    public IRadioAudioEngine? Current => Volatile.Read(ref _current);
    public double Volume => _volume;
    public TimeSpan Duration => Current?.Duration ?? TimeSpan.Zero;
    public event EventHandler<MediaPlaybackState>? StateChanged;
    public event EventHandler? Completed;
    public event EventHandler? Failed;

    public void Replace(IRadioAudioEngine? engine, bool disposePrevious = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(engine, Current)) return;
        var previous = Interlocked.Exchange(ref _current, null);
        Interlocked.Increment(ref _generation);
        try
        {
            if (previous is not null)
            {
                previous.StateChanged -= Engine_StateChanged;
                previous.Completed -= Engine_Completed;
                previous.Failed -= Engine_Failed;
                if (disposePrevious) previous.Dispose();
            }
        }
        catch { engine?.Dispose(); throw; }
        if (engine is null) return;
        try
        {
            engine.SetVolume(_volume);
            Volatile.Write(ref _current, engine);
            engine.StateChanged += Engine_StateChanged;
            engine.Completed += Engine_Completed;
            engine.Failed += Engine_Failed;
        }
        catch
        {
            Volatile.Write(ref _current, null);
            engine.StateChanged -= Engine_StateChanged;
            engine.Completed -= Engine_Completed;
            engine.Failed -= Engine_Failed;
            engine.Dispose();
            throw;
        }
    }

    public void SetVolume(double value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        Current?.SetVolume(_volume);
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Current?.Play();
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Current?.Pause();
    }

    private void Deliver(object? sender, Action action)
    {
        var generation = Interlocked.Read(ref _generation);
        if (!ReferenceEquals(sender, Current)) return;
        dispatch(() =>
        {
            if (generation == Interlocked.Read(ref _generation) && ReferenceEquals(sender, Current)) action();
        });
    }

    private void Engine_StateChanged(object? sender, MediaPlaybackState state) => Deliver(sender, () => StateChanged?.Invoke(this, state));
    private void Engine_Completed(object? sender, EventArgs args) => Deliver(sender, () => Completed?.Invoke(this, args));
    private void Engine_Failed(object? sender, EventArgs args) => Deliver(sender, () => Failed?.Invoke(this, args));

    public void Dispose()
    {
        if (_disposed) return;
        try { Replace(null); }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
