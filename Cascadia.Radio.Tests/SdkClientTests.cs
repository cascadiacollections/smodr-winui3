using System.Net;
using Cascadia.RadioBrowser;

namespace smodr.Tests;

[TestClass]
public sealed class SdkClientTests
{
    private static readonly Guid _uuid = Guid.Parse("12345678-1234-1234-1234-123456789012");
    private static readonly Uri[] _servers = [new("https://one.example/"), new("https://two.example/")];
    private const string Valid = """[{"stationuuid":"one","name":"Radio","url_resolved":"https://radio.example/live","countrycode":"US"}]""";

    [TestMethod]
    public async Task ReadsFailOverAndKeepWireModelsIndependent()
    {
        using var handler = new Handler(request => Reply(request.RequestUri!.Host == "one.example" ? "not json" : Valid));
        using var http = new HttpClient(handler);
        var client = Client(http);
        var stations = await client.SearchAsync(new SearchOptions { Name = "jazz & blues", CountryCode = "US", Offset = 20 });
        Assert.HasCount(1, stations);
        Assert.AreEqual("US", stations[0].CountryCode);
        Assert.HasCount(2, handler.Requests);
        StringAssert.Contains(handler.Requests[1].Query, "name=jazz%20%26%20blues", StringComparison.Ordinal);
        StringAssert.Contains(handler.Requests[1].Query, "offset=20", StringComparison.Ordinal);
        Assert.AreEqual(0, http.DefaultRequestHeaders.UserAgent.Count);
        Assert.AreEqual("Consumer/1.0", handler.UserAgent);
    }

    [TestMethod]
    public async Task ExcessiveRowsAndTruncatedBodiesFailOver()
    {
        foreach (var payload in new[] { "[" + string.Join(',', Enumerable.Repeat("{}", 1001)) + "]", Valid })
        {
            using var handler = new Handler(request =>
            {
                var response = Reply(request.RequestUri!.Host == "one.example" ? payload : Valid);
                if (request.RequestUri.Host == "one.example" && payload == Valid) response.Content.Headers.ContentLength = 1;
                return response;
            });
            using var http = new HttpClient(handler);
            Assert.HasCount(1, await Client(http).GetRankedAsync());
            Assert.HasCount(2, handler.Requests);
        }
    }

    [TestMethod]
    public async Task MutationsDoNotRetryOrFailOver()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(http).RegisterClickAsync(_uuid));
        Assert.HasCount(1, handler.Requests);
        StringAssert.EndsWith(handler.Requests[0].AbsolutePath, _uuid.ToString("D"), StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ReadsNeverReportClicksAndUuidLookupUsesDocumentedQuery()
    {
        using var handler = new Handler(_ => Reply(Valid));
        using var http = new HttpClient(handler);
        var client = Client(http);
        await client.GetByUuidAsync(_uuid);
        await client.GetByUrlAsync(new Uri("https://radio.example/live?q=a&b=c"));
        Assert.AreEqual("/json/stations/byuuid", handler.Requests[0].AbsolutePath);
        StringAssert.Contains(handler.Requests[0].Query, $"uuids={_uuid:D}", StringComparison.Ordinal);
        Assert.IsFalse(handler.Requests.Any(uri => uri.AbsolutePath.StartsWith("/json/url/", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("""{"ok":true}""")]
    [DataRow("""{"ok":"true"}""")]
    public async Task ExplicitClicksAcceptBothServerSuccessShapes(string json)
    {
        using var handler = new Handler(_ => Reply(json));
        using var http = new HttpClient(handler);
        await Client(http).RegisterClickAsync(_uuid);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CancellationDuringTransportNeverContactsSecondMirror()
    {
        using var stop = new CancellationTokenSource();
        using var handler = new Handler(_ => { stop.Cancel(); stop.Token.ThrowIfCancellationRequested(); return Reply(Valid); });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Client(http).GetRankedAsync(cancellationToken: stop.Token));
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task WholeRequestDeadlineIncludesHeaders()
    {
        using var handler = new StallHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new RadioBrowserClient(http, new Mirrors([_servers[0]]),
            new ClientOptions { UserAgent = "Consumer/1.0", RequestTimeout = TimeSpan.FromMilliseconds(50) });
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => client.GetRankedAsync().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public async Task FacetsAndAllRankingsAreTypedAndBounded()
    {
        using var handler = new Handler(request => Reply(request.RequestUri!.AbsolutePath.Contains("/stations/", StringComparison.Ordinal)
            ? Valid : """[{"name":"US","stationcount":123}]"""));
        using var http = new HttpClient(handler);
        var client = Client(http);
        foreach (var rank in Enum.GetValues<StationRanking>()) Assert.HasCount(1, await client.GetRankedAsync(rank));
        foreach (var facet in Enum.GetValues<DirectoryFacet>())
            Assert.AreEqual(123, (await client.GetValuesAsync(facet))[0].StationCount);
        Assert.HasCount(8, handler.Requests);
    }

    [TestMethod]
    public async Task DiscoveryIsCoalescedAndCallerCancellationIsIsolated()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<Uri>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new DnsMirrorProvider(_ => { Interlocked.Increment(ref calls); return pending.Task; });
        using var stop = new CancellationTokenSource();
        var first = provider.GetServersAsync(stop.Token);
        var second = provider.GetServersAsync();
        await stop.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        pending.SetResult(new Uri[] { new("https://good.api.radio-browser.info/"), new("https://evil.example/") });
        Assert.AreEqual("good.api.radio-browser.info", (await second).Single().Host);
        await provider.GetServersAsync();
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task InvalidInputsAndThrowingDiagnosticsDoNotLeakRequestsOrBreakFailover()
    {
        using var handler = new Handler(request => Reply(request.RequestUri!.Host == "one.example" ? "{}" : Valid));
        using var http = new HttpClient(handler);
        var client = new RadioBrowserClient(http, new Mirrors(_servers), new ClientOptions { UserAgent = "Consumer/1.0" }, diagnostics: new BadDiagnostics());
        Assert.HasCount(1, await client.GetRankedAsync());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => client.SearchAsync(new SearchOptions { Limit = 0 }));
        Assert.ThrowsExactly<ArgumentException>(() => client.GetByUuidAsync(Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => client.GetByUrlAsync(new Uri("file:///local")));
        Assert.HasCount(2, handler.Requests);
    }

    private static RadioBrowserClient Client(HttpClient http) => new(http, new Mirrors(_servers), new ClientOptions { UserAgent = "Consumer/1.0" });
    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Mirrors(IReadOnlyList<Uri> servers) : IMirrorProvider
    {
        public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default) => Task.FromResult(servers);
    }
    private sealed class BadDiagnostics : IClientDiagnostics
    {
        public void Record(ClientDiagnostic diagnostic) => throw new InvalidOperationException();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string? UserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(respond(request));
        }
    }
    private sealed class StallHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
