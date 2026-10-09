using smodr.Services;
using Windows.Media.Playback;

namespace smodr.Tests.Fixtures;

/// <summary>No native objects or audio device; disposal counts deliberately detect double ownership.</summary>
internal sealed class TestRadioEngine(RadioAudioEngineKind kind) : IRadioAudioEngine
{
    private int _disposals;
    public int Disposals => Volatile.Read(ref _disposals);
    public int Plays { get; private set; }
    public double Volume { get; private set; }
    public bool RejectDisposal { get; init; }
    public RadioAudioEngineKind Kind => kind;
    public RadioEqualizerPreset Preset => RadioEqualizerPreset.Off;
    public MediaPlaybackState State { get; private set; }
    public TimeSpan Duration => TimeSpan.FromSeconds(120);
    public TimeSpan Position => TimeSpan.Zero;
    public event EventHandler<MediaPlaybackState>? StateChanged;
    public event EventHandler? Completed;
    public event EventHandler? Failed;

    public void Play()
    {
        Plays++;
        EmitState(MediaPlaybackState.Playing);
    }

    public void Pause()
    {
        EmitState(MediaPlaybackState.Paused);
    }

    public void SetVolume(double volume)
    {
        Volume = volume;
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _disposals);
        GC.SuppressFinalize(this);
        if (RejectDisposal)
        {
            throw new InvalidOperationException("Synthetic native teardown failure.");
        }
    }

    public void EmitState(MediaPlaybackState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void EmitCompleted()
    {
        Completed?.Invoke(this, EventArgs.Empty);
    }

    public void EmitFailed()
    {
        Failed?.Invoke(this, EventArgs.Empty);
    }
}
