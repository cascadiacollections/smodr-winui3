using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class PlaybackSleepTimerTests
{
    [TestMethod]
    public void DependencyInjectionConstructsDefaultTimer()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<PlaybackSleepTimer>()
            .BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<PlaybackSleepTimer>());
    }

    [TestMethod]
    public async Task TimerFiresOnceAndClearsItsDeadline()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        using var timer = new PlaybackSleepTimer(clock);
        var fired = 0;
        var firedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        timer.Elapsed += (_, _) =>
        {
            Interlocked.Increment(ref fired);
            firedSignal.TrySetResult();
        };

        timer.Start(TimeSpan.FromMinutes(15));
        Assert.IsNotNull(timer.EndsAt);
        Assert.IsTrue(timer.Remaining > TimeSpan.Zero);
        clock.Advance(TimeSpan.FromMinutes(15));
        await firedSignal.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(1, Volatile.Read(ref fired));
        Assert.IsNull(timer.EndsAt);
        Assert.IsNull(timer.Remaining);
    }

    [TestMethod]
    public async Task CancelPreventsElapsedEvenIfDelayCompletesLate()
    {
        var delays = new ControlledDelays();
        using var timer = new PlaybackSleepTimer(delay: delays.WaitAsync);
        var fired = 0;
        timer.Elapsed += (_, _) => Interlocked.Increment(ref fired);

        timer.Start(TimeSpan.FromMinutes(15));
        timer.Cancel();
        delays.Complete(0);
        await delays.Completed(0);
        await Task.Delay(20);

        Assert.AreEqual(0, Volatile.Read(ref fired));
        Assert.IsNull(timer.EndsAt);
    }

    [TestMethod]
    public async Task ReplacingTimerInvalidatesOriginalAndUsesNewDeadline()
    {
        var delays = new ControlledDelays();
        using var timer = new PlaybackSleepTimer(delay: delays.WaitAsync);
        var fired = 0;
        timer.Elapsed += (_, _) => Interlocked.Increment(ref fired);

        timer.Start(TimeSpan.FromMinutes(15));
        timer.Start(TimeSpan.FromMinutes(30));
        delays.Complete(0);
        await delays.Completed(0);
        await Task.Delay(20);
        Assert.AreEqual(0, Volatile.Read(ref fired));
        Assert.IsNotNull(timer.EndsAt);

        delays.Complete(1);
        await delays.Completed(1);
        await Task.Delay(20);
        Assert.AreEqual(1, Volatile.Read(ref fired));
    }

    [TestMethod]
    public async Task DisposalPreventsElapsedAndFurtherStarts()
    {
        var delays = new ControlledDelays();
        var timer = new PlaybackSleepTimer(delay: delays.WaitAsync);
        var fired = 0;
        timer.Elapsed += (_, _) => Interlocked.Increment(ref fired);
        timer.Start(TimeSpan.FromMinutes(15));
        timer.Dispose();
        delays.Complete(0);
        await delays.Completed(0);
        await Task.Delay(20);

        Assert.AreEqual(0, Volatile.Read(ref fired));
        Assert.ThrowsExactly<ObjectDisposedException>(() => timer.Start(TimeSpan.FromMinutes(15)));
    }

    private sealed class ControlledDelays
    {
        private readonly List<TaskCompletionSource> _completed = [];
        private readonly List<TaskCompletionSource> _delays = [];

        public Task WaitAsync(TimeSpan _, CancellationToken token)
        {
            var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delays.Add(delay);
            _completed.Add(completed);
            return WaitCoreAsync(delay.Task, completed);
        }

        public void Complete(int index)
        {
            _delays[index].SetResult();
        }

        public Task Completed(int index)
        {
            return _completed[index].Task.WaitAsync(TimeSpan.FromSeconds(3));
        }

        private static async Task WaitCoreAsync(Task delay, TaskCompletionSource completed)
        {
            try { await delay; }
            finally { completed.TrySetResult(); }
        }
    }
}
