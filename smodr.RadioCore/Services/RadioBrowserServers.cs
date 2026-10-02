using System.Net;
using System.Net.Sockets;

namespace smodr.Services;

public interface IRadioBrowserServerProvider
{
    Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default);
}

/// <summary>Cached DNS discovery with randomized, HTTPS mirror failover.</summary>
public sealed class RadioBrowserServers(
    Func<CancellationToken, Task<IReadOnlyList<Uri>>>? discover = null,
    TimeProvider? clock = null) : IRadioBrowserServerProvider, IDisposable
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly Func<CancellationToken, Task<IReadOnlyList<Uri>>> _discover = discover ?? DiscoverAsync;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Uri[] _cached = [];
    private DateTimeOffset _refreshAfter;

    public async Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clock.GetUtcNow() >= _refreshAfter)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var discovered = await _discover(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                    var servers = discovered.Where(IsMirror).Distinct().Take(16).ToArray();
                    if (servers.Length == 0) throw new InvalidDataException("No Radio Browser mirrors discovered.");
                    _cached = servers;
                    _refreshAfter = _clock.GetUtcNow() + TimeSpan.FromHours(6);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException or OperationCanceledException)
                {
                    AppDiagnostics.Record("directory.dns", exception);
                    // Reuse known mirrors during DNS outages; a first launch can
                    // still try the official aggregate hostname through a proxy.
                    if (_cached.Length == 0) _cached = [new Uri("https://all.api.radio-browser.info/")];
                    _refreshAfter = _clock.GetUtcNow() + TimeSpan.FromMinutes(1);
                }
            }

            var shuffled = (Uri[])_cached.Clone();
            Random.Shared.Shuffle(shuffled);
            return shuffled;
        }
        finally { _refresh.Release(); }
    }

    public void Dispose() => _refresh.Dispose();

    private static bool IsMirror(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/"
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.Host.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync("all.api.radio-browser.info", cancellationToken).ConfigureAwait(false);
        var lookups = addresses.Distinct().Take(16).Select(async address =>
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(address).WaitAsync(cancellationToken).ConfigureAwait(false);
                return Uri.TryCreate($"https://{entry.HostName.TrimEnd('.')}/", UriKind.Absolute, out var uri) ? uri : null;
            }
            catch (SocketException) { return null; }
        });
        return (await Task.WhenAll(lookups).ConfigureAwait(false)).OfType<Uri>().ToArray();
    }
}
