using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;
using smodr.Tests.Fixtures;
using Windows.Media.Playback;

namespace smodr.Tests.Services;

// Ownership is deliberately transferred to the coordinator/slot and checked for exactly one disposal.
#pragma warning disable CA2000
[TestClass]
[TestCategory("RuntimeRace")]
public sealed class RadioRuntimeRaceTests
{
    [TestMethod]
    public void NativeTeardownFailureDoesNotLeakReplacementOrReopenDisposedCoordinator()
    {
        var coordinator = new RadioAudioEngineCoordinator(action => action());
        var first = new TestRadioEngine(RadioAudioEngineKind.MediaPlayer) { RejectDisposal = true };
        var replacement = new TestRadioEngine(RadioAudioEngineKind.AudioGraph);
        coordinator.Replace(first);
        Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Replace(replacement));
        Assert.IsNull(coordinator.Current);
        Assert.AreEqual(1, first.Disposals);
        Assert.AreEqual(1, replacement.Disposals, typeof(RadioAudioEngineCoordinator).Assembly.Location);
        var failing = new TestRadioEngine(RadioAudioEngineKind.AudioGraph) { RejectDisposal = true };
        coordinator.Replace(failing);
        Assert.ThrowsExactly<InvalidOperationException>(coordinator.Dispose);
        coordinator.Dispose();
        Assert.AreEqual(1, failing.Disposals);
        Assert.ThrowsExactly<ObjectDisposedException>(coordinator.Play);
    }

    [TestMethod]
    public void RapidStationSwitchDropsAllRetiredStartupAndFailureCallbacks()
    {
        var dispatch = new Queue<Action>();
        using var coordinator = new RadioAudioEngineCoordinator(dispatch.Enqueue);
        var engines = new List<TestRadioEngine>();
        var states = 0;
        var failures = 0;
        var completions = 0;
        coordinator.StateChanged += (_, _) => states++;
        coordinator.Failed += (_, _) => failures++;
        coordinator.Completed += (_, _) => completions++;
        for (var index = 0; index < 128; index++)
        {
            var engine = new TestRadioEngine(index % 2 == 0 ? RadioAudioEngineKind.MediaPlayer : RadioAudioEngineKind.AudioGraph);
            engines.Add(engine);
            coordinator.Replace(engine);
            engine.EmitState(MediaPlaybackState.Opening);
            engine.EmitFailed();
            engine.EmitCompleted();
        }
        while (dispatch.TryDequeue(out var callback)) callback();
        Assert.AreEqual(1, states);
        Assert.AreEqual(1, failures);
        Assert.AreEqual(1, completions);
        coordinator.Dispose();
        Assert.IsTrue(engines.All(engine => engine.Disposals == 1));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PauseOrSwitchInvalidatesRetryAlreadyWaitingOnOwnerDispatcher(bool switchStation)
    {
        var clock = new FakeTimeProvider();
        var retries = Channel.CreateUnbounded<long>();
        using var recovery = new LiveRadioRecovery(epoch => retries.Writer.TryWrite(epoch), _ => Assert.Fail("Unexpected exhaustion"), clock);
        recovery.Begin();
        recovery.Fail();
        clock.Advance(TimeSpan.FromSeconds(2));
        var queuedEpoch = await retries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        if (switchStation) recovery.Begin();
        else recovery.Pause();
        Assert.IsFalse(recovery.IsCurrent(queuedEpoch));
        Assert.AreEqual(switchStation, recovery.IsRequested);
        recovery.Dispose();
        clock.Advance(TimeSpan.FromDays(1));
        Assert.IsFalse(retries.Reader.TryRead(out _));
    }

    [TestMethod]
    public void ShutdownDuringOpeningRejectsCallbacksAndFurtherControls()
    {
        var dispatch = new Queue<Action>();
        var coordinator = new RadioAudioEngineCoordinator(dispatch.Enqueue);
        var engine = new TestRadioEngine(RadioAudioEngineKind.AudioGraph);
        var deliveries = 0;
        coordinator.StateChanged += (_, _) => deliveries++;
        coordinator.Failed += (_, _) => deliveries++;
        coordinator.Completed += (_, _) => deliveries++;
        coordinator.Replace(engine);
        engine.EmitState(MediaPlaybackState.Opening);
        engine.EmitFailed();
        engine.EmitCompleted();
        coordinator.Dispose();
        while (dispatch.TryDequeue(out var callback)) callback();
        engine.EmitFailed();
        Assert.AreEqual(0, deliveries);
        Assert.AreEqual(1, engine.Disposals);
        Assert.ThrowsExactly<ObjectDisposedException>(coordinator.Play);
        Assert.ThrowsExactly<ObjectDisposedException>(coordinator.Pause);
        Assert.ThrowsExactly<ObjectDisposedException>(() => coordinator.SetVolume(0.5));
    }

    [TestMethod]
    public void PreparedHandoffAndDspBridgeHaveExactlyOneOwnerAtEveryTransition()
    {
        using var slot = new RadioStreamWarmupSlot<TestRadioEngine>();
        using var coordinator = new RadioAudioEngineCoordinator(action => action());
        var prepared = new TestRadioEngine(RadioAudioEngineKind.MediaPlayer);
        slot.Put("https://example.com/stream", prepared);
        var transferred = slot.Take("https://example.com/stream", allowed: true);
        Assert.AreSame(prepared, transferred);
        coordinator.Replace(transferred);
        slot.Clear();
        Assert.AreEqual(0, prepared.Disposals);
        Assert.IsNull(slot.Take("https://example.com/stream", allowed: true));
        var bridge = new TestRadioEngine(RadioAudioEngineKind.MediaPlayer);
        coordinator.Replace(bridge);
        Assert.AreEqual(1, prepared.Disposals);
        var graph = new TestRadioEngine(RadioAudioEngineKind.AudioGraph);
        coordinator.Replace(graph, disposePrevious: false);
        coordinator.Play();
        Assert.AreEqual(1, graph.Plays);
        Assert.AreEqual(0, bridge.Plays);
        coordinator.Dispose();
        bridge.Dispose(); // Owner retains this source-less SMTC capability separately.
        Assert.AreEqual(1, graph.Disposals);
        Assert.AreEqual(1, bridge.Disposals);
    }

    [TestMethod]
    public async Task ConcurrentTakeClearAndShutdownNeverDoubleDisposePreparedResource()
    {
        for (var index = 0; index < 100; index++)
        {
            var slot = new RadioStreamWarmupSlot<TestRadioEngine>();
            var resource = new TestRadioEngine(RadioAudioEngineKind.MediaPlayer);
            slot.Put("stream", resource);
            var take = Task.Run(() => slot.Take("stream", allowed: true));
            await Task.WhenAll(take, Task.Run(slot.Clear), Task.Run(slot.Dispose));
            (await take)?.Dispose();
            Assert.AreEqual(1, resource.Disposals);
        }
    }
}
