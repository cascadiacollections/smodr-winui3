using System.Net;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioDirectoryServiceTests
{
    private static readonly string[] _expectedFallbackHosts = ["first.example", "second.example"];

    [TestMethod]
    public async Task SearchFallsBackToNextServerAndFiltersInvalidStations()
    {
        var requests = new List<Uri>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.Host == "first.example"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {"stationuuid":"one","name":"Jazz FM","url_resolved":"https://example.com/live"},
                          {"stationuuid":"one","name":"Duplicate","url_resolved":"https://example.com/other"},
                          {"stationuuid":"bad","name":"Bad","url_resolved":"file:///local"}
                        ]
                        """)
                };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        var stations = await service.SearchAsync("jazz & blues");

        Assert.HasCount(2, requests);
        Assert.AreEqual("second.example", requests[1].Host);
        StringAssert.Contains(requests[1].Query, "jazz%20%26%20blues", StringComparison.Ordinal);
        Assert.HasCount(1, stations);
        Assert.AreEqual("Jazz FM", stations[0].Name);
    }

    [TestMethod]
    public async Task AllServersFailWithFriendlyError()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client, (Uri[])[new Uri("https://first.example/")]);

        await Assert.ThrowsExactlyAsync<RadioDirectoryUnavailableException>(
            () => service.GetPopularStationsAsync());
    }

    [TestMethod]
    public async Task InvalidJsonFallsBackToNextServer()
    {
        var hosts = new List<string>();
        using var handler = new StubHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.Host == "first.example"
                    ? "{invalid-json"
                    : "[]")
            };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        var stations = await service.GetPopularStationsAsync();

        Assert.IsEmpty(stations);
        CollectionAssert.AreEqual(_expectedFallbackHosts, hosts);
    }

    [TestMethod]
    public async Task OversizedAdvertisedResponseFallsBack()
    {
        var hosts = new List<string>();
        using var handler = new StubHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            var content = new StringContent("[]");
            if (request.RequestUri.Host == "first.example")
                content.Headers.ContentLength = 3 * 1024 * 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        Assert.IsEmpty(await service.GetPopularStationsAsync());
        CollectionAssert.AreEqual(_expectedFallbackHosts, hosts);
    }

    [TestMethod]
    public async Task ResponseLargerThanAdvertisedLengthFallsBack()
    {
        var hosts = new List<string>();
        using var handler = new StubHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            var content = new StringContent(request.RequestUri.Host == "first.example"
                ? $"[{new string(' ', 2 * 1024 * 1024)}]" : "[]");
            if (request.RequestUri.Host == "first.example") content.Headers.ContentLength = 2;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        Assert.IsEmpty(await service.GetPopularStationsAsync());
        CollectionAssert.AreEqual(_expectedFallbackHosts, hosts);
    }

    [TestMethod]
    public async Task ExcessStationCountFallsBack()
    {
        var hosts = new List<string>();
        using var handler = new StubHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.Host == "first.example"
                    ? $"[{string.Join(',', Enumerable.Repeat("{}", 1_001))}]" : "[]")
            };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        Assert.IsEmpty(await service.GetPopularStationsAsync());
        CollectionAssert.AreEqual(_expectedFallbackHosts, hosts);
    }

    [TestMethod]
    public async Task ValidOverlongResponseReturnsAtMostOneHundredStations()
    {
        var entries = Enumerable.Range(0, 150).Select(index =>
            $"{{\"stationuuid\":\"{index}\",\"name\":\"Station {index}\",\"url_resolved\":\"https://example.com/{index}\"}}");
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"[{string.Join(',', entries)}]")
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client, (Uri[])[new Uri("https://first.example/")]);

        var stations = await service.GetPopularStationsAsync(100);

        Assert.HasCount(100, stations);
        Assert.AreEqual("Station 0", stations[0].Name);
        Assert.AreEqual("Station 99", stations[^1].Name);
    }

    [TestMethod]
    public async Task UserCancellationDoesNotContactFallbackServer()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        using var handler = new CancellationHandler(() =>
        {
            requests++;
            cancellation.Cancel();
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetPopularStationsAsync(cancellationToken: cancellation.Token));

        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task DiscoveryUsesIosClickRanking()
    {
        var requests = new List<Uri>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client, (Uri[])[new Uri("https://first.example/")]);

        await service.GetPopularStationsAsync();
        await service.SearchAsync("jazz");
        await service.SearchGenreAsync("jazz");

        StringAssert.Contains(requests[0].AbsolutePath, "/topclick/", StringComparison.Ordinal);
        StringAssert.Contains(requests[1].Query, "order=clickcount", StringComparison.Ordinal);
        StringAssert.Contains(requests[2].Query, "order=clickcount", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ReportPlaySendsOnlyRadioBrowserUuidAndFallsBack()
    {
        var requests = new List<Uri>();
        using var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(request.RequestUri!.Host == "first.example"
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var service = new RadioDirectoryService(client,
            (Uri[])[new Uri("https://first.example/"), new Uri("https://second.example/")]);

        await service.ReportPlayAsync("bundled-kexp");
        Assert.IsEmpty(requests);
        await service.ReportPlayAsync("bdb9fa3b-5672-4e0e-9b75-dcb19295c483");

        Assert.HasCount(2, requests);
        Assert.AreEqual("/json/url/bdb9fa3b-5672-4e0e-9b75-dcb19295c483", requests[0].AbsolutePath);
        Assert.AreEqual("second.example", requests[1].Host);
        Assert.IsTrue(client.DefaultRequestHeaders.UserAgent.Count > 0);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class CancellationHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }
}
