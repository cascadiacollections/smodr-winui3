using System.Net;

namespace smodr.Services;

/// <summary>Reads every ICY block on a cancellable sidecar connection, without touching audio playback.</summary>
public sealed class IcyMetadataStreamReader(HttpClient client) : IContinuousTrackMetadataReader
{
    private static readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(35);

    public async Task<bool> ListenAsync(Uri streamUri, Action<string> onMetadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        ArgumentNullException.ThrowIfNull(onMetadata);
        if (!streamUri.IsAbsoluteUri || streamUri.Scheme is not ("http" or "https")) return false;

        using var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        request.Headers.UserAgent.ParseAdd("ShoutkitWindows/0.1");
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerTimeout.CancelAfter(_readTimeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            headerTimeout.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) response.EnsureSuccessStatusCode();
        if (!IcyMetadataProbe.TryGetInterval(response.Headers, response.Content.Headers, out var interval))
            return false;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var audio = new byte[4096];
        var length = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = interval;
            while (remaining > 0)
            {
                var read = await ReadWithTimeoutAsync(stream,
                    audio.AsMemory(0, Math.Min(audio.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) return true;
                remaining -= read;
            }
            if (await ReadWithTimeoutAsync(stream, length, cancellationToken).ConfigureAwait(false) == 0)
                return true;
            var metadataLength = length[0] * 16;
            if (metadataLength == 0) continue;
            var metadata = new byte[metadataLength];
            var offset = 0;
            while (offset < metadata.Length)
            {
                var read = await ReadWithTimeoutAsync(stream, metadata.AsMemory(offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) return true;
                offset += read;
            }
            var raw = IcyMetadataProbe.Decode(metadata).TrimEnd('\0').Trim();
            if (raw.Length > 0) onMetadata(raw);
        }
        return true;
    }

    private static async Task<int> ReadWithTimeoutAsync(Stream stream, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readTimeout);
        return await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
    }
}
