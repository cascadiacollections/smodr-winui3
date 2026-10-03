namespace smodr.Services;

/// <summary>Registers work before invoking it; shutdown seals admission, cancels, and drains every accepted operation.</summary>
public sealed class BackgroundWorkScope : IDisposable, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _pending = [];
    private TaskCompletionSource? _stopping;

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync<object?>(async token => { await operation(token).ConfigureAwait(false); return null; });
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping is not null, this);
            token = _lifetime.Token;
            _pending.Add(completion.Task);
        }
        _ = ExecuteAsync(operation, completion, token);
        return completion.Task;
    }

    private async Task ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
        TaskCompletionSource<T> completion, CancellationToken token)
    {
        try { completion.TrySetResult(await operation(token).ConfigureAwait(false)); }
        catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
        catch (Exception exception)
        {
            AppDiagnostics.Record("background.work", exception);
            completion.TrySetException(exception);
            _ = completion.Task.Exception; // Observe fire-and-forget faults; awaited callers still receive them.
        }
        finally { lock (_gate) _pending.Remove(completion.Task); }
    }

    public Task StopAsync()
    {
        Task[] pending;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_stopping is not null) return _stopping.Task;
            completion = _stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = [.. _pending];
        }
        _ = StopCoreAsync(pending, completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(Task[] pending, TaskCompletionSource completion)
    {
        try { await _lifetime.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) { AppDiagnostics.Record("background.cancel", exception); }
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (Exception) { /* Individual work faults are observed above; cancellation is normal during shutdown. */ }
        finally { _lifetime.Dispose(); completion.TrySetResult(); }
    }

    public void Dispose() { _ = StopAsync(); GC.SuppressFinalize(this); }
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
}
