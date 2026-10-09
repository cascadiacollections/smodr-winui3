using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class BackgroundWorkScopeTests
{
    [TestMethod]
    public async Task ShutdownSealsAdmissionCancelsAndWaitsForAcceptedCleanup()
    {
        await using var scope = new BackgroundWorkScope();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var operation = scope.RunAsync(async token =>
        {
            captured = token;
            await cleanup.Task;
            return 42;
        });
        var stopping = scope.StopAsync();
        Assert.IsTrue(captured.IsCancellationRequested);
        Assert.IsFalse(stopping.IsCompleted);
        Assert.AreSame(stopping, scope.StopAsync());
        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.RunAsync(_ => Task.CompletedTask));
        cleanup.SetResult();
        Assert.AreEqual(42, await operation);
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task SynchronousFailureAndCanceledWorkDoNotPoisonDrain()
    {
        await using var scope = new BackgroundWorkScope();
        var failed = scope.RunAsync<int>(_ => throw new InvalidOperationException("Synthetic"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        var canceled = scope.RunAsync(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<TaskCanceledException>(() => canceled);
    }

    [TestMethod]
    public async Task ConcurrentAdmissionAndStopNeverLeaveAnUntrackedAcceptedTask()
    {
        for (var iteration = 0; iteration < 32; iteration++)
        {
            await using var scope = new BackgroundWorkScope();
            var finished = 0;
            var workers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await scope.RunAsync(async token =>
                    {
                        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                        finally { Interlocked.Increment(ref finished); }
                    });
                }
                catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException)
                {
                }
            })).ToArray();
            await scope.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var afterDrain = Volatile.Read(ref finished);
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(afterDrain, Volatile.Read(ref finished));
        }
    }
}
