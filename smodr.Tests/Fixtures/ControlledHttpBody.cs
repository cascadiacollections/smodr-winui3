namespace smodr.Tests.Fixtures;

internal sealed class ControlledHttpBody(byte[] bytes, bool stalled = false) : Stream
{
    private int _disposed;
    private int _offset;
    public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _offset; set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadEntered.TrySetResult();
        if (stalled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Min(buffer.Length, bytes.Length - _offset);
        bytes.AsMemory(_offset, length).CopyTo(buffer);
        _offset += length;
        return length;
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Exchange(ref _disposed, 1);
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}

internal sealed class ControlledHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return send(request, cancellationToken);
    }
}
