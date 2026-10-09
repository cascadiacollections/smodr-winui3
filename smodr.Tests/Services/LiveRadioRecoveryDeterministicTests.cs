using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("RuntimeRace")]
public sealed class LiveRadioRecoveryDeterministicTests
{
    [TestMethod]
    public async Task QueuedResumeCannotRestartAfterPauseOrStationChange()
    {
        var clock = new FakeTimeProvider();
        var restarts = Channel.CreateUnbounded<long>();
        using var recovery = new LiveRadioRecovery(epoch => restarts.Writer.TryWrite(epoch), _ => { }, clock);
        recovery.ResumePending();
        clock.Advance(TimeSpan.FromSeconds(2));
        var retiredEpoch = await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        recovery.Pause();
        Assert.IsFalse(recovery.IsCurrent(retiredEpoch));
        recovery.Begin();
        Assert.IsFalse(recovery.IsCurrent(retiredEpoch));
        Assert.IsTrue(recovery.IsRequested);
    }

    [TestMethod]
    public async Task BriefPlayingFlapsCannotReplenishRetryBudget()
    {
        var clock = new FakeTimeProvider();
        var restarts = Channel.CreateUnbounded<long>();
        var exhausted = 0;
        using var recovery = new LiveRadioRecovery(epoch => restarts.Writer.TryWrite(epoch),
            _ => exhausted++, clock, maxRetries: 2);
        recovery.Begin();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            recovery.Fail();
            clock.Advance(TimeSpan.FromSeconds(2 << attempt));
            await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            recovery.Playing();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        recovery.Fail();
        Assert.AreEqual(1, exhausted);
        Assert.IsFalse(recovery.IsRequested);
    }

    [TestMethod]
    public async Task SustainedPlaybackReplenishesBudgetWithoutResettingOnDuplicateEvents()
    {
        var clock = new FakeTimeProvider();
        var restarts = Channel.CreateUnbounded<long>();
        using var recovery = new LiveRadioRecovery(epoch => restarts.Writer.TryWrite(epoch),
            _ => Assert.Fail("Stable playback must earn a new retry budget."), clock, maxRetries: 1);
        recovery.Begin();
        recovery.Fail();
        clock.Advance(TimeSpan.FromSeconds(2));
        await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        recovery.Playing();
        clock.Advance(TimeSpan.FromSeconds(20));
        recovery.Playing();
        clock.Advance(TimeSpan.FromSeconds(10));
        recovery.Fail();
        clock.Advance(TimeSpan.FromSeconds(2));
        var epoch = await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(recovery.IsCurrent(epoch));
    }

    [TestMethod]
    public void InvalidTimerConfigurationFailsSynchronously()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LiveRadioRecovery(_ => { }, _ => { }, stallTimeout: TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LiveRadioRecovery(_ => { }, _ => { }, resumeTimeout: TimeSpan.FromSeconds(-1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LiveRadioRecovery(_ => { }, _ => { }, retryBaseDelay: TimeSpan.FromDays(1), maxRetries: 10));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new LiveRadioRecovery(_ => { }, _ => { }, stablePlaybackWindow: TimeSpan.Zero));
    }

    [TestMethod]
    public async Task RepeatedBufferingDoesNotExtendStallDeadline()
    {
        var clock = new FakeTimeProvider();
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = new LiveRadioRecovery(_ => { }, _ => { }, clock, beforeRetry: _ => retired.TrySetResult());
        recovery.Begin();
        clock.Advance(TimeSpan.FromSeconds(20));
        recovery.Buffering();
        clock.Advance(TimeSpan.FromSeconds(10));
        await retired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(recovery.IsRequested);
    }
}
