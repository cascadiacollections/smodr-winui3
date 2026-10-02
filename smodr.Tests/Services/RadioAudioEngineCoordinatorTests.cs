using smodr.Services;
using Windows.Media.Playback;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioAudioEngineCoordinatorTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BothEnginesUseCommonControlsAndPreserveVolume(bool graph)
    {
        using var coordinator = new RadioAudioEngineCoordinator(action => action());
        using var engine = new FakeEngine(graph ? RadioAudioEngineKind.AudioGraph : RadioAudioEngineKind.MediaPlayer);
        coordinator.SetVolume(0.8);
        coordinator.Replace(engine);
        Assert.AreEqual(0.8, engine.Volume);
        Assert.AreEqual(0, engine.Plays);
        coordinator.Play();
        coordinator.Pause();
        Assert.AreEqual(1, engine.Plays);
        Assert.AreEqual(1, engine.Pauses);
        Assert.AreEqual(TimeSpan.FromSeconds(120), coordinator.Duration);
        coordinator.SetVolume(4);
        Assert.AreEqual(1d, engine.Volume);
        coordinator.SetVolume(double.NaN);
        Assert.AreEqual(0d, engine.Volume);
    }

    [TestMethod]
    public void QueuedCallbacksCannotReachNewEngineEvenWhenSameInstanceReturns()
    {
        var queued = new Queue<Action>();
        using var coordinator = new RadioAudioEngineCoordinator(queued.Enqueue);
        using var first = new FakeEngine(RadioAudioEngineKind.MediaPlayer);
        using var second = new FakeEngine(RadioAudioEngineKind.AudioGraph);
        var changes = 0;
        var completed = 0;
        var failed = 0;
        coordinator.StateChanged += (_, _) => changes++;
        coordinator.Completed += (_, _) => completed++;
        coordinator.Failed += (_, _) => failed++;
        coordinator.Replace(first);
        first.Play();
        first.EmitCompleted();
        first.EmitFailed();
        coordinator.Replace(second, disposePrevious: false);
        coordinator.Replace(first, disposePrevious: false);
        while (queued.TryDequeue(out var action)) action();
        Assert.AreEqual(0, changes);
        Assert.AreEqual(0, completed);
        Assert.AreEqual(0, failed);
        first.Play();
        first.EmitCompleted();
        first.EmitFailed();
        while (queued.TryDequeue(out var action)) action();
        Assert.AreEqual(1, changes);
        Assert.AreEqual(1, completed);
        Assert.AreEqual(1, failed);
    }

    [TestMethod]
    public void NormalReplacementAndShutdownDisposeOnceAndUnsubscribe()
    {
        using var coordinator = new RadioAudioEngineCoordinator(action => action());
        using var first = new FakeEngine(RadioAudioEngineKind.MediaPlayer);
        using var second = new FakeEngine(RadioAudioEngineKind.AudioGraph);
        var failures = 0;
        coordinator.Failed += (_, _) => failures++;
        coordinator.Replace(first);
        coordinator.Replace(second);
        Assert.AreEqual(1, first.Disposals);
        first.EmitFailed();
        Assert.AreEqual(0, failures);
        coordinator.Dispose();
        coordinator.Dispose();
        second.EmitFailed();
        Assert.AreEqual(1, second.Disposals);
        Assert.AreEqual(0, failures);
        Assert.AreEqual(TimeSpan.Zero, coordinator.Duration);
        using var late = new FakeEngine(RadioAudioEngineKind.MediaPlayer);
        Assert.ThrowsExactly<ObjectDisposedException>(() => coordinator.Replace(late));
    }

    [TestMethod]
    public void SourceLessBridgeOwnershipCanBeRetainedSeparatelyWithoutReceivingDecoderCallbacks()
    {
        using var coordinator = new RadioAudioEngineCoordinator(action => action());
        using var bridge = new FakeEngine(RadioAudioEngineKind.MediaPlayer);
        using var graph = new FakeEngine(RadioAudioEngineKind.AudioGraph);
        var failures = 0;
        coordinator.Failed += (_, _) => failures++;
        coordinator.Replace(bridge);
        coordinator.Replace(graph, disposePrevious: false);
        Assert.AreEqual(0, bridge.Disposals);
        bridge.EmitFailed();
        Assert.AreEqual(0, failures);
        graph.EmitFailed();
        Assert.AreEqual(1, failures);
        coordinator.Replace(null);
        Assert.AreEqual(1, graph.Disposals);
        Assert.AreEqual(0, bridge.Disposals); // AudioService owns this source-less SMTC host until player teardown.
    }

    [TestMethod]
    public void FailedEngineBindingRetiresIncomingEngineAndDoesNotRetainIt()
    {
        using var coordinator = new RadioAudioEngineCoordinator(action => action());
        using var engine = new FakeEngine(RadioAudioEngineKind.MediaPlayer) { RejectVolume = true };
        Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Replace(engine));
        Assert.IsNull(coordinator.Current);
        Assert.AreEqual(1, engine.Disposals);
    }

    private sealed class FakeEngine(RadioAudioEngineKind kind) : IRadioAudioEngine
    {
        public RadioAudioEngineKind Kind => kind;
        public RadioEqualizerPreset Preset => RadioEqualizerPreset.Off;
        public MediaPlaybackState State { get; private set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(120);
        public TimeSpan Position => TimeSpan.Zero;
        public double Volume { get; private set; }
        public bool RejectVolume { get; init; }
        public int Plays { get; private set; }
        public int Pauses { get; private set; }
        public int Disposals { get; private set; }
        public event EventHandler<MediaPlaybackState>? StateChanged;
        public event EventHandler? Completed;
        public event EventHandler? Failed;
        public void Play() { Plays++; State = MediaPlaybackState.Playing; StateChanged?.Invoke(this, State); }
        public void Pause() { Pauses++; State = MediaPlaybackState.Paused; StateChanged?.Invoke(this, State); }
        public void SetVolume(double volume) { if (RejectVolume) throw new InvalidOperationException("Synthetic failure"); Volume = volume; }
        public void EmitCompleted() => Completed?.Invoke(this, EventArgs.Empty);
        public void EmitFailed() => Failed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Disposals++; GC.SuppressFinalize(this); }
    }
}
