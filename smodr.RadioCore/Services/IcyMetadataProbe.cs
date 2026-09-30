using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace smodr.Services;

/// <summary>Reads only through the first ICY metadata block, then closes the probe connection.</summary>
public sealed class IcyMetadataProbe(HttpClient httpClient) : ITrackMetadataProbe
{
    private const int MaxMetadataInterval = 65_536;
    private const int MaxMetadataLength = 255 * 16;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    public async Task<IcyProbeResult> ProbeAsync(Uri streamUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        if (!streamUri.IsAbsoluteUri || streamUri.Scheme is not ("http" or "https"))
            return IcyProbeResult.Unsupported;

        using var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        request.Headers.UserAgent.ParseAdd("ShoutkitWindows/0.1");
        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!TryGetInterval(response.Headers, response.Content.Headers, out var interval))
            return IcyProbeResult.Unsupported;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[4096];
        var remaining = interval;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0) return new IcyProbeResult(true, null);
            remaining -= read;
        }

        var lengthByte = new byte[1];
        if (await stream.ReadAsync(lengthByte, cancellationToken).ConfigureAwait(false) == 0)
            return new IcyProbeResult(true, null);
        var metadataLength = lengthByte[0] * 16;
        if (metadataLength == 0) return new IcyProbeResult(true, null);
        if (metadataLength > MaxMetadataLength) return new IcyProbeResult(true, null);
        var metadata = new byte[metadataLength];
        await stream.ReadExactlyAsync(metadata, cancellationToken).ConfigureAwait(false);
        var raw = Decode(metadata).TrimEnd('\0').Trim();
        return new IcyProbeResult(true, raw.Length == 0 ? null : raw);
    }

    private static bool TryGetInterval(HttpResponseHeaders responseHeaders,
        HttpContentHeaders contentHeaders, out int interval)
    {
        interval = 0;
        var values = responseHeaders.TryGetValues("icy-metaint", out var responseValues)
            ? responseValues
            : contentHeaders.TryGetValues("icy-metaint", out var contentValues)
                ? contentValues
                : null;
        return values is not null
            && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out interval)
            && interval is > 0 and <= MaxMetadataInterval;
    }

    private static string Decode(byte[] bytes)
    {
        try { return _strictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
