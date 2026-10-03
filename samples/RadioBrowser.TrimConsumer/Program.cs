using System.Net;
using Cascadia.RadioBrowser;

using var handler = new FixtureHandler();
using var http = new HttpClient(handler);
var client = new RadioBrowserClient(http, new FixtureMirrors(), new ClientOptions { UserAgent = "TrimConsumer/1.0" });
if ((await client.GetRankedAsync()).Single().Name != "Trim radio") throw new InvalidDataException("Trimmed JSON consumer failed.");
if ((await client.GetValuesAsync(DirectoryFacet.Tags)).Single().StationCount != 1) throw new InvalidDataException("Trimmed facets failed.");
await client.RegisterClickAsync(Guid.Parse("12345678-1234-1234-1234-123456789012"));
Console.WriteLine("Offline trimmed Radio Browser consumer passed.");

internal sealed class FixtureMirrors : IMirrorProvider
{
    private static readonly IReadOnlyList<Uri> _servers = Array.AsReadOnly(new Uri[] { new("https://fixture.example/") });
    public Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default) => Task.FromResult(_servers);
}
internal sealed class FixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.StartsWith("/json/url/", StringComparison.Ordinal)
                ? """{"ok":"true"}"""
                : request.RequestUri.AbsolutePath.StartsWith("/json/stations/", StringComparison.Ordinal)
                    ? """[{"stationuuid":"fixture","name":"Trim radio","url_resolved":"https://fixture.example/live"}]"""
                    : """[{"name":"jazz","stationcount":1}]""")
        });
}
