using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace smodr.Tests.Fixtures;

/// <summary>Per-test loopback-only HTTP fixture. No URL reservations, files, external stations or audio output.</summary>
internal sealed class SyntheticRadioServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _accepting;
    private int _requests;
    private int _recoverAttempts;
    public Uri Address { get; }
    public int Requests => Volatile.Read(ref _requests);
    public TaskCompletionSource StallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> MetadataHeadersObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SyntheticRadioServer()
    {
        _listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync();
    }

    public Uri UriFor(string path) => new(Address, path);

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(ServeAsync(connection));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient connection)
    {
        using (connection)
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(requestTimeout.Token) ?? string.Empty;
                var path = request.Split(' ').ElementAtOrDefault(1) ?? "/";
                var metadata = false;
                var userAgent = false;
                var headerBytes = request.Length;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(requestTimeout.Token)))
                {
                    headerBytes += header.Length;
                    if (headerBytes > 8192) throw new IOException("Fixture request headers too large.");
                    metadata |= header.Equals("Icy-MetaData: 1", StringComparison.OrdinalIgnoreCase);
                    userAgent |= header.StartsWith("User-Agent: ShoutkitWindows/", StringComparison.OrdinalIgnoreCase);
                }
                Interlocked.Increment(ref _requests);
                MetadataHeadersObserved.TrySetResult(metadata && userAgent);
                if (path == "/redirect")
                {
                    await HeaderAsync(stream, "302 Found", "Location: /icy\r\n", _stop.Token);
                    return;
                }
                if (path == "/recover" && Interlocked.Increment(ref _recoverAttempts) < 3)
                {
                    await HeaderAsync(stream, "503 Service Unavailable", string.Empty, _stop.Token);
                    return;
                }
                if (path == "/wav")
                {
                    var wav = SilentWave();
                    await HeaderAsync(stream, "200 OK", $"Content-Type: audio/wav\r\nContent-Length: {wav.Length}\r\n", _stop.Token);
                    await stream.WriteAsync(wav, _stop.Token);
                    return;
                }
                await HeaderAsync(stream, "200 OK", "Content-Type: audio/mpeg\r\nicy-metaint: 64\r\n", _stop.Token);
                if (path == "/stall")
                {
                    StallStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                    return;
                }
                string[] titles = path == "/damaged"
                    ? ["Artist - First", "H\uFFFDsker D\uFFFD - Ice Cold Ice", "Artist - Second"]
                    : ["Hüsker Dü - Ice Cold Ice", "Artist - Rapid Second"];
                var body = new List<byte>();
                foreach (var title in titles) AddIcyBlock(body, title);
                if (path == "/truncated") body.RemoveRange(body.Count - 7, 7);
                var bytes = body.ToArray();
                // Deliberately split interval bytes, length bytes, metadata and multibyte UTF-8 across writes.
                for (var offset = 0; offset < bytes.Length; offset += 3)
                    await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(3, bytes.Length - offset)), _stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException) { }
        }
    }

    private static async Task HeaderAsync(Stream stream, string status, string extra, CancellationToken token) =>
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\n{extra}\r\n"), token);

    private static void AddIcyBlock(List<byte> body, string title)
    {
        body.AddRange(new byte[64]);
        var metadata = Encoding.UTF8.GetBytes($"StreamTitle='{title}';");
        var blocks = (metadata.Length + 15) / 16;
        body.Add((byte)blocks);
        body.AddRange(metadata);
        body.AddRange(new byte[blocks * 16 - metadata.Length]);
    }

    private static byte[] SilentWave()
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + 16000);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // Mono
        writer.Write(8000);
        writer.Write(16000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(16000);
        writer.Write(new byte[16000]);
        return buffer.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _accepting;
        _listener.Stop();
        await Task.WhenAll(_connections).WaitAsync(TimeSpan.FromSeconds(3));
        _stop.Dispose();
    }
}
