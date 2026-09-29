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
