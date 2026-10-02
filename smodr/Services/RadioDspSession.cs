using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Render;

namespace smodr.Services;

/// <summary>Opt-in AudioGraph pipeline. The source feeds the equalizer and output, never a second decoder.</summary>
internal sealed class RadioDspSession : IDisposable
{
    private readonly AudioGraph _graph;
    private readonly MediaSourceAudioInputNode _input;
    private readonly MediaSource _source;
    private int _disposed;
    public RadioEqualizerPreset Preset { get; }
    public TimeSpan Duration => _input.Duration;
    public TimeSpan Position => _input.Position;
    public event EventHandler? Completed;
    public event EventHandler? Failed;

    private RadioDspSession(AudioGraph graph, MediaSourceAudioInputNode input, MediaSource source,
        RadioEqualizerPreset preset)
    {
        _graph = graph;
        _input = input;
        _source = source;
        Preset = preset;
        _input.MediaSourceCompleted += InputCompleted;
        _graph.UnrecoverableErrorOccurred += GraphFailed;
    }

    private void InputCompleted(MediaSourceAudioInputNode sender, object args)
    {
        if (Volatile.Read(ref _disposed) == 0) Completed?.Invoke(this, EventArgs.Empty);
    }

    private void GraphFailed(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args)
    {
        if (Volatile.Read(ref _disposed) == 0 && args.Error != AudioGraphUnrecoverableError.None)
            Failed?.Invoke(this, EventArgs.Empty);
    }

    public static async Task<RadioDspSession> CreateAsync(Uri uri, RadioEqualizerPreset preset,
        CancellationToken cancellationToken)
    {
        AudioGraph? graph = null;
        MediaSource? source = null;
        try
        {
            var created = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media)).AsTask(cancellationToken);
            if (created.Status != AudioGraphCreationStatus.Success) throw new InvalidOperationException("AudioGraph creation failed.");
            graph = created.Graph;
            var output = await graph.CreateDeviceOutputNodeAsync().AsTask(cancellationToken);
            if (output.Status != AudioDeviceNodeCreationStatus.Success) throw new InvalidOperationException("Audio output unavailable.");
            source = MediaSource.CreateFromUri(uri);
            var input = await graph.CreateMediaSourceAudioInputNodeAsync(source).AsTask(cancellationToken);
            if (input.Status != MediaSourceAudioInputNodeCreationStatus.Success) throw new InvalidOperationException("DSP source unsupported.");
            var equalizer = new EqualizerEffectDefinition(graph);
            var bands = RadioEqualizerProfiles.Bands(preset);
            if (equalizer.Bands.Count != bands.Count) throw new InvalidOperationException("Unexpected equalizer band count.");
            for (var index = 0; index < bands.Count; index++)
            {
                equalizer.Bands[index].FrequencyCenter = bands[index].Frequency;
                equalizer.Bands[index].Gain = bands[index].Gain;
            }
            input.Node.EffectDefinitions.Add(equalizer);
            input.Node.AddOutgoingConnection(output.DeviceOutputNode);
            cancellationToken.ThrowIfCancellationRequested();
            return new RadioDspSession(graph, input.Node, source, preset);
        }
        catch
        {
            graph?.Dispose();
            source?.Dispose();
            throw;
        }
    }

    public void SetVolume(double volume) => _input.OutgoingGain = Math.Clamp(volume, 0, 1) * RadioEqualizerProfiles.Headroom(Preset);
    public void Play() => _graph.Start();
    public void Pause() => _graph.Stop();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _input.MediaSourceCompleted -= InputCompleted;
        _graph.UnrecoverableErrorOccurred -= GraphFailed;
        try { _graph.Stop(); }
        finally
        {
            try { _graph.Dispose(); }
            finally { _source.Dispose(); }
        }
        GC.SuppressFinalize(this);
    }
}
