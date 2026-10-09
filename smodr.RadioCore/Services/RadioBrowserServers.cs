using Cascadia.RadioBrowser;

namespace smodr.Services;

public interface IRadioBrowserServerProvider
{
    Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default);
}

public sealed class RadioBrowserServers(
    Func<CancellationToken, Task<IReadOnlyList<Uri>>>? discover = null,
    TimeProvider? clock = null) : IRadioBrowserServerProvider, IDisposable
{
    private readonly DnsMirrorProvider _provider = new(discover, clock);
    private int _disposed;

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _provider.GetServersAsync(cancellationToken);
    }
}
