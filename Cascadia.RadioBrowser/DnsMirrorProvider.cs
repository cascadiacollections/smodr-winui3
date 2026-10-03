using System.Net;
using System.Net.Sockets;

namespace Cascadia.RadioBrowser;

/// <summary>Coalesced bounded discovery. Caller cancellation never cancels other callers' refresh.</summary>
public sealed class DnsMirrorProvider(
    Func<CancellationToken, Task<IReadOnlyList<Uri>>>? discover = null,
    TimeProvider? clock = null, IClientDiagnostics? diagnostics = null) : IMirrorProvider
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Func<CancellationToken, Task<IReadOnlyList<Uri>>> _discover = discover ?? DiscoverAsync;
    private Uri[] _cached = [];
    private DateTimeOffset _refreshAfter;
    private Task? _refresh;

    public async Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? refresh;
        lock (_gate)
        {
            if (_clock.GetUtcNow() >= _refreshAfter && (_refresh is null || _refresh.IsCompleted))
                _refresh = RefreshAsync();
            refresh = _refresh;
        }
        if (refresh is not null) await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var servers = (Uri[])_cached.Clone();
            Random.Shared.Shuffle(servers);
            return Array.AsReadOnly(servers);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var result = await _discover(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            var servers = result.Where(IsMirror).Distinct().Take(16).ToArray();
            if (servers.Length == 0) throw new InvalidDataException("No official mirrors found.");
            lock (_gate)
            {
                _cached = servers;
                _refreshAfter = _clock.GetUtcNow() + TimeSpan.FromHours(6);
            }
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidDataException or OperationCanceledException)
        {
            try { diagnostics?.Record(ClientDiagnostic.DiscoveryFailed); }
            catch (Exception) { /* Diagnostics cannot break discovery. */ }
            lock (_gate)
            {
                if (_cached.Length == 0) _cached = [new Uri("https://all.api.radio-browser.info/")];
                _refreshAfter = _clock.GetUtcNow() + TimeSpan.FromMinutes(1);
            }
        }
    }

    private static bool IsMirror(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/"
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.Host.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<Uri>> DiscoverAsync(CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync("all.api.radio-browser.info", token).ConfigureAwait(false);
        var lookups = addresses.Distinct().Take(16).Select(async address =>
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(address).WaitAsync(token).ConfigureAwait(false);
                return Uri.TryCreate($"https://{entry.HostName.TrimEnd('.')}/", UriKind.Absolute, out var uri) ? uri : null;
            }
            catch (SocketException) { return null; }
        });
        return (await Task.WhenAll(lookups).ConfigureAwait(false)).OfType<Uri>().ToArray();
    }
}

