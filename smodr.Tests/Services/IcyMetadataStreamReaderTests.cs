using System.Net;
using System.Text;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class IcyMetadataStreamReaderTests
{
    [TestMethod]
    public async Task EmitsEveryNonemptyBlockIncludingRapidTitleChanges()
    {
        var body = new List<byte>();
        AddBlock(body, string.Empty);
        AddBlock(body, "StreamTitle='Artist - First';");
        AddBlock(body, "StreamTitle='Artist - Second';");
        using var handler = new StubHandler(_ => Response([.. body]));
        using var client = new HttpClient(handler);
        var titles = new List<string>();

        var supported = await new IcyMetadataStreamReader(client).ListenAsync(
            new Uri("https://example.com/live"), titles.Add);

        Assert.IsTrue(supported);
        Assert.HasCount(2, titles);
        Assert.AreEqual("StreamTitle='Artist - First';", titles[0]);
        Assert.AreEqual("StreamTitle='Artist - Second';", titles[1]);
    }

    [TestMethod]
    public async Task TruncatedBlockDoesNotEmitPartialTitle()
    {
        var body = new List<byte>();
        AddBlock(body, "StreamTitle='Artist - Complete';");
        AddBlock(body, "StreamTitle='Artist - Partial';");
        body.RemoveRange(body.Count - 9, 9);
        using var handler = new StubHandler(_ => Response([.. body]));
        using var client = new HttpClient(handler);
        var titles = new List<string>();

        await new IcyMetadataStreamReader(client).ListenAsync(new Uri("https://example.com/live"), titles.Add);

        Assert.HasCount(1, titles);
        Assert.AreEqual("StreamTitle='Artist - Complete';", titles[0]);
    }

    [TestMethod]
    public async Task UnsupportedResponseDoesNotEmitMetadata()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        using var client = new HttpClient(handler);
        Assert.IsFalse(await new IcyMetadataStreamReader(client).ListenAsync(
            new Uri("https://example.com/live"), _ => Assert.Fail("Unexpected cue")));
    }

    [TestMethod]
    public async Task Latin1MetadataDecodesAndCancellationStopsReader()
    {
        var body = new List<byte>();
        AddBlock(body, "StreamTitle='Björk - Jóga';", Encoding.Latin1);
        using var handler = new StubHandler(_ => Response([.. body]));
        using var client = new HttpClient(handler);
        var titles = new List<string>();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new IcyMetadataStreamReader(client).ListenAsync(new Uri("https://example.com/live"), raw =>
        {
            titles.Add(raw);
            cancellation.Cancel();
        }, cancellation.Token));
        Assert.HasCount(1, titles);
        Assert.AreEqual("StreamTitle='Björk - Jóga';", titles[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreservesHuskerDuAcrossUtf8AndLatin1Blocks(bool latin1)
    {
        const string metadata = "StreamTitle='Hüsker Dü - Ice Cold Ice';";
        var body = new List<byte>();
        AddBlock(body, metadata, latin1 ? Encoding.Latin1 : Encoding.UTF8);
        using var handler = new StubHandler(_ => Response([.. body]));
        using var client = new HttpClient(handler);
        var titles = new List<string>();
        await new IcyMetadataStreamReader(client).ListenAsync(new Uri("https://example.com/live"), titles.Add);
        Assert.HasCount(1, titles);
        Assert.AreEqual(metadata, titles[0]);
        Assert.AreEqual("Hüsker Dü", IcyTrackParser.Parse(titles[0], "KEXP")?.Artist);
    }

    [TestMethod]
    public async Task Utf8CharactersSplitAcrossNetworkReadsRemainIntact()
    {
        const string metadata = "StreamTitle='Hüsker Dü - Ice Cold Ice';";
        var body = new List<byte>();
        AddBlock(body, metadata);
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FragmentedStream([.. body]))
            };
            response.Headers.TryAddWithoutValidation("icy-metaint", "4");
            return response;
        });
        using var client = new HttpClient(handler);
        var titles = new List<string>();
        await new IcyMetadataStreamReader(client).ListenAsync(new Uri("https://example.com/live"), titles.Add);
        Assert.HasCount(1, titles);
        Assert.AreEqual(metadata, titles[0]);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private static HttpResponseMessage Response(byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Headers.TryAddWithoutValidation("icy-metaint", "4");
        return response;
    }

    private static void AddBlock(List<byte> body, string metadata, Encoding? encoding = null)
    {
        body.AddRange(new byte[4]);
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(metadata);
        var blocks = (bytes.Length + 15) / 16;
        body.Add((byte)blocks);
        body.AddRange(bytes);
        body.AddRange(new byte[blocks * 16 - bytes.Length]);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
