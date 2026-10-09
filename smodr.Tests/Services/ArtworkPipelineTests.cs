using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class ArtworkPipelineTests
{
    [TestMethod]
    public void WeightedLruExpiresAndDoesNotRetainOversizeImages()
    {
        var clock = new FakeTimeProvider();
        var cache = new ArtworkMemoryCache<string>(2, 6, clock);
        cache.Put("first", "one", 3);
        cache.Put("second", "two", 3);
        Assert.IsTrue(cache.TryGet("first", out _));
        cache.Put("third", "three", 3);
        Assert.IsFalse(cache.TryGet("second", out _));
        cache.Put("large", "large", 7);
        Assert.IsFalse(cache.TryGet("large", out _));
        cache.Put("first", "replacement", 1);
        Assert.IsTrue(cache.TryGet("first", out var replacement));
        Assert.AreEqual("replacement", replacement);
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.IsFalse(cache.TryGet("first", out _));
    }

    [TestMethod]
    [DataRow(52d, 2d, 128)]
    [DataRow(320d, 2d, 640)]
    [DataRow(10_000d, 3d, 1024)]
    [DataRow(0d, 0d, 64)]
    [DataRow(double.NaN, double.NaN, 64)]
    public void DecodeSizeUsesDisplayScaleAndBoundedBuckets(double size, double scale, int expected)
    {
        Assert.AreEqual(expected, ArtworkSizing.DecodeEdge(size, scale));
    }

    [TestMethod]
    public void DecodeDimensionsPreserveAspectRatioAndRejectImageBombs()
    {
        Assert.AreEqual((128, 64), ArtworkSizing.DecodeDimensions(2048, 1024, 128));
        Assert.IsNull(ArtworkSizing.DecodeDimensions(8192, 8192, 128));
        Assert.IsNull(ArtworkSizing.DecodeDimensions(1, 9000, 128));
        Assert.IsNull(ArtworkSizing.DecodeDimensions(0, 64, 128));
    }

    [TestMethod]
    public async Task ValidArtworkIsCachedAndTransportIsNotRepeated()
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Response([1, 2, 3]));
        });
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client);
        var uri = new Uri("https://art.example/cover");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await loader.GetAsync(uri));
        await loader.GetAsync(uri);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow("http://localhost/cover")]
    [DataRow("https://user:password@art.example/cover")]
    [DataRow("file:///C:/private.png")]
    public async Task UnsafeArtworkDoesNotMakeRequests(string value)
    {
        using var handler = new Handler((_, _) => throw new AssertFailedException("Unexpected request"));
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client);
        Assert.IsNull(await loader.GetAsync(new Uri(value)));
    }

    [TestMethod]
    public async Task UnsupportedContentAndAdvertisedOrUnadvertisedOversizeAreRejected()
    {
        using var handler = new Handler((request, _) =>
        {
            var response = Response(new byte[StationArtworkLoader.MaxArtworkBytes + 1]);
            if (request.RequestUri!.AbsolutePath == "/html")
            {
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            }

            if (request.RequestUri.AbsolutePath == "/declared")
            {
                response.Content.Headers.ContentLength = StationArtworkLoader.MaxArtworkBytes + 1;
            }

            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client);
        foreach (var path in new[] { "html", "declared", "unknown" })
        {
            Assert.IsNull(await loader.GetAsync(new Uri("https://art.example/" + path)));
        }
    }

    [TestMethod]
    public async Task DeadlineCoversBodyAndUserCancellationStillPropagates()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response([], true)));
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client, timeout: TimeSpan.FromMilliseconds(40));
        Assert.IsNull(await loader.GetAsync(new Uri("https://art.example/stall")).WaitAsync(TimeSpan.FromSeconds(3)));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            loader.GetAsync(new Uri("https://art.example/cancel"), cancellation.Token));
    }

    [TestMethod]
    public async Task DownloadConcurrencyIsBoundedAndQueuedRequestsCanCancel()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 4)
            {
                started.SetResult();
            }

            await release.Task.WaitAsync(token);
            return Response([1]);
        });
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client);
        var pending = Enumerable.Range(0, 4).Select(index => loader.GetAsync(new Uri($"https://art.example/{index}")))
            .ToArray();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancellation = new CancellationTokenSource();
            var queued = loader.GetAsync(new Uri("https://art.example/queued"), cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued);
            Assert.AreEqual(4, Volatile.Read(ref calls));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(pending);
        }
    }

    [TestMethod]
    [DataRow("https://art.example/final", true)]
    [DataRow("http://art.example/final", false)]
    [DataRow("http://127.0.0.1/private", false)]
    [DataRow("https://user:secret@art.example/final", false)]
    public async Task RedirectsValidateDestinationAndRejectDowngrades(string location, bool accepted)
    {
        var calls = 0;
        using var handler = new Handler((_, _) =>
        {
            calls++;
            if (calls > 1)
            {
                return Task.FromResult(Response([1]));
            }

            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var loader = new StationArtworkLoader(client);
        Assert.AreEqual(accepted, await loader.GetAsync(new Uri("https://art.example/redirect")) is not null);
        Assert.AreEqual(accepted ? 2 : 1, calls);
    }

    private static HttpResponseMessage Response(byte[] bytes, bool hung = false)
    {
        var content = new StreamContent(new Body(bytes, hung));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }

    private sealed class Body(byte[] bytes, bool hung) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (hung)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
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
}
