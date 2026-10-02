namespace smodr.Services;

/// <summary>Optional, bounded artwork transport. The deadline includes response-body reads and queue time.</summary>
public sealed class StationArtworkLoader(HttpClient client, StationArtworkDiskCache? diskCache = null,
    TimeSpan? timeout = null) : IDisposable
{
    public const int MaxArtworkBytes = 1_000_000;
    private readonly ArtworkMemoryCache<byte[]> _cache = new(48, 8 * 1024 * 1024);
    private readonly SemaphoreSlim _downloads = new(4);

    public async Task<byte[]?> GetAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafe(uri)) return null;
        if (_cache.TryGet(uri.AbsoluteUri, out var cached)) return cached;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        try
        {
            if (diskCache is not null)
            {
                var saved = await Task.Run(() => diskCache.TryReadAsync(uri, token), token).ConfigureAwait(false);
                if (saved is not null) { _cache.Put(uri.AbsoluteUri, saved, saved.Length); return saved; }
            }
            await _downloads.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_cache.TryGet(uri.AbsoluteUri, out cached)) return cached;
                using var response = await GetResponseAsync(uri, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxArtworkBytes
                    || response.Content.Headers.ContentType?.MediaType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp")) return null;
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[16_384];
                int read;
                while ((read = await source.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > MaxArtworkBytes) return null;
                    await buffer.WriteAsync(chunk.AsMemory(0, read), token).ConfigureAwait(false);
                }
                if (buffer.Length == 0) return null;
                var bytes = buffer.ToArray();
                _cache.Put(uri.AbsoluteUri, bytes, bytes.Length);
                if (diskCache is not null) await Task.Run(() => diskCache.StoreAsync(uri, bytes, token), token).ConfigureAwait(false);
                return bytes;
            }
            finally { _downloads.Release(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException) { return null; }
    }

    public void Dispose() { _downloads.Dispose(); GC.SuppressFinalize(this); }

    private static bool IsSafe(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && !uri.IsLoopback;

    private async Task<HttpResponseMessage> GetResponseAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; ; redirects++)
        {
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || redirects == 3) return response;
            if (response.Headers.Location is not { } location || !Uri.TryCreate(uri, location, out var next)
                || !IsSafe(next) || (uri.Scheme == "https" && next.Scheme != "https")) return response;
            response.Dispose();
            uri = next;
        }
    }
}
