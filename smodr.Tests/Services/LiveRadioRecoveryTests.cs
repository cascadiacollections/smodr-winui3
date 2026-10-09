using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class LiveRadioRecoveryTests
{
    [TestMethod]
    public async Task StalledStreamRetriesWithBoundedBudgetThenReportsFailure()
    {
        var exhausted = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restarts = 0;
        using var recovery = NewRecovery(
            _ => Interlocked.Increment(ref restarts),
            epoch => exhausted.TrySetResult(epoch),
            maxRetries: 2);

        recovery.Begin();
        var epoch = await exhausted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(2, Volatile.Read(ref restarts));
        Assert.IsFalse(recovery.IsRequested);
        Assert.IsTrue(recovery.IsEpoch(epoch));
    }

    [TestMethod]
    public async Task PauseCancelsScheduledReconnect()
    {
        var restarted = false;
        using var recovery = NewRecovery(_ => restarted = true, _ => { },
            TimeSpan.FromSeconds(1), retry: TimeSpan.FromMilliseconds(60));

        recovery.Begin();
        recovery.Fail();
        recovery.Pause();
        await Task.Delay(150);

        Assert.IsFalse(restarted);
        Assert.IsFalse(recovery.IsRequested);
    }

    [TestMethod]
    public async Task NewStationInvalidatesOldReconnect()
    {
        var restarted = 0;
        using var recovery = NewRecovery(_ => Interlocked.Increment(ref restarted), _ => { },
            TimeSpan.FromSeconds(1), retry: TimeSpan.FromMilliseconds(80));

        recovery.Begin();
        recovery.Fail();
        recovery.Begin();
        await Task.Delay(160);

        Assert.AreEqual(0, Volatile.Read(ref restarted));
        Assert.IsTrue(recovery.IsRequested);
    }

    [TestMethod]
    public async Task PlayingCancelsStallCeiling()
    {
        var restarted = false;
        using var recovery = NewRecovery(_ => restarted = true, _ => { },
            TimeSpan.FromMilliseconds(40));

        recovery.Begin();
        recovery.Playing();
        await Task.Delay(120);

        Assert.IsFalse(restarted);
        Assert.IsTrue(recovery.IsRequested);
    }

    [TestMethod]
    public async Task SilentResumeTriggersFreshStreamAndOldEpochCannotRestart()
    {
        var restarted = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = NewRecovery(epoch => restarted.TrySetResult(epoch), _ => { },
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(30));

        recovery.Begin();
        recovery.Pause();
        recovery.ResumePending();
        var epoch = await restarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(recovery.IsCurrent(epoch));
        recovery.Pause();
        Assert.IsFalse(recovery.IsCurrent(epoch));
    }

    [TestMethod]
    public async Task PlayingInvalidatesAlreadyQueuedRestart()
    {
        var restarted = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = NewRecovery(epoch => restarted.TrySetResult(epoch), _ => { },
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(25));

        recovery.Begin();
        recovery.ResumePending();
        var oldEpoch = await restarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        recovery.Playing();

        Assert.IsFalse(recovery.IsCurrent(oldEpoch));
        Assert.IsTrue(recovery.IsRequested);
    }

    [TestMethod]
    public async Task StallRetiresOldStreamBeforeRestart()
    {
        var calls = new List<string>();
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recovery = new LiveRadioRecovery(
            _ =>
            {
                lock (calls)
                {
                    calls.Add("restart");
                }

                restarted.TrySetResult();
            },
            _ => { },
            stallTimeout: TimeSpan.FromMilliseconds(25),
            retryBaseDelay: TimeSpan.FromMilliseconds(25),
            beforeRetry: _ =>
            {
                lock (calls)
                {
                    calls.Add("retire");
                }
            });

        recovery.Begin();
        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        lock (calls)
        {
            Assert.AreEqual("retire,restart", string.Join(',', calls));
        }
    }

    private static LiveRadioRecovery NewRecovery(
        Action<long> restart,
        Action<long> exhausted,
        TimeSpan? stall = null,
        TimeSpan? resume = null,
        TimeSpan? retry = null,
        int maxRetries = 3)
    {
        return new LiveRadioRecovery(
            restart,
            exhausted,
            stallTimeout: stall ?? TimeSpan.FromMilliseconds(25),
            resumeTimeout: resume ?? TimeSpan.FromMilliseconds(25),
            retryBaseDelay: retry ?? TimeSpan.FromMilliseconds(20),
            maxRetries: maxRetries);
    }
}
