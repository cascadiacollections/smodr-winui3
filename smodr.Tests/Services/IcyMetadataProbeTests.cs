using System.Net;
using System.Text;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class IcyMetadataProbeTests
{
    [TestMethod]
    public async Task ReadsFirstMetadataBlockAndRequestsIcy()
    {
        const string metadata = "StreamTitle='Artist - Song';";
        var encoded = Encoding.UTF8.GetBytes(metadata);
        var blocks = (encoded.Length + 15) / 16;
        var body = new byte[4 + 1 + blocks * 16];
        encoded.CopyTo(body, 5);
        body[4] = (byte)blocks;
        using var handler = new StubHandler(request =>
        {
            Assert.AreEqual("1", request.Headers.GetValues("Icy-MetaData").Single());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
            response.Headers.TryAddWithoutValidation("icy-metaint", "4");
            return response;
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var result = await new IcyMetadataProbe(client).ProbeAsync(new Uri("https://example.com/live"));
        Assert.IsTrue(result.IsSupported);
        Assert.AreEqual(metadata, result.RawMetadata);
    }

    [TestMethod]
    public async Task SkipsEmptyPrerollAndFindsLaterSongTitle()
    {
        var body = new List<byte>();
        AddBlock(body, "StreamTitle='';StreamUrl='';adw_ad='true';");
        // KEXP's live preroll has placed the first actual song at block 55.
        for (var index = 0; index < 53; index++) AddBlock(body, null);
        AddBlock(body, "StreamTitle='Alice in Chains - Brother';StreamUrl='Sap';");
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. body])
            };
            response.Headers.TryAddWithoutValidation("icy-metaint", "4");
            return response;
        });
        using var client = new HttpClient(handler, disposeHandler: false);

        var result = await new IcyMetadataProbe(client).ProbeAsync(new Uri("https://example.com/live"));

        Assert.IsTrue(result.IsSupported);
        Assert.AreEqual("StreamTitle='Alice in Chains - Brother';StreamUrl='Sap';", result.RawMetadata);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreservesAccentsAndSkipsAlreadyDamagedCues(bool latin1)
    {
        const string metadata = "StreamTitle='Hüsker Dü - Ice Cold Ice';";
        var body = new List<byte>();
        AddBlock(body, "StreamTitle='H\uFFFDsker D\uFFFD - Ice Cold Ice';");
        AddBlock(body, metadata, latin1 ? Encoding.Latin1 : Encoding.UTF8);
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. body])
            };
            response.Headers.TryAddWithoutValidation("icy-metaint", "4");
            return response;
        });
        using var client = new HttpClient(handler);
        var result = await new IcyMetadataProbe(client).ProbeAsync(new Uri("https://example.com/live"));
        Assert.AreEqual(metadata, result.RawMetadata);
    }

    private static void AddBlock(List<byte> body, string? metadata, Encoding? encoding = null)
    {
        body.AddRange(new byte[4]);
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(metadata ?? string.Empty);
        var blocks = (bytes.Length + 15) / 16;
        body.Add((byte)blocks);
        body.AddRange(bytes);
        body.AddRange(new byte[blocks * 16 - bytes.Length]);
    }

    [TestMethod]
    public async Task UnsupportedStreamDoesNotReadBody()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var result = await new IcyMetadataProbe(client).ProbeAsync(new Uri("https://example.com/live"));
        Assert.IsFalse(result.IsSupported);
        Assert.IsNull(result.RawMetadata);
    }

    [TestMethod]
    public async Task OversizedIntervalIsRejected()
    {
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            };
            response.Headers.TryAddWithoutValidation("icy-metaint", "99999999");
            return response;
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var result = await new IcyMetadataProbe(client).ProbeAsync(new Uri("https://example.com/live"));
        Assert.IsFalse(result.IsSupported);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
