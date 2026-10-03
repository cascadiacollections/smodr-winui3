using System.Net;
using Cascadia.RadioBrowser;
using smodr.Models;
using smodr.Services;

// Offline by design: this sample verifies the packages, not the public service.
using var handler = new FixtureHandler();
using var http = new HttpClient(handler);
var client = new RadioBrowserClient(http, new FixtureMirrors(), new ClientOptions { UserAgent = "RadioSdkConsumer/1.0" });
var station = (await client.SearchAsync(new SearchOptions { Name = "Synthetic radio" })).Single();
var track = IcyTrackParser.Parse("StreamTitle='Hüsker Dü - Ice Cold Ice';", station.Name)
    ?? throw new InvalidDataException("ICY package consumer failed.");
if (track.Artist != "Hüsker Dü" || track.Title != "Ice Cold Ice") throw new InvalidDataException("Unicode metadata failed.");
var path = Path.Combine(Path.GetTempPath(), $"radio-sdk-consumer-{Guid.NewGuid():N}.json");
try
{
    var library = new RadioLibraryService(path);
    await library.ToggleFavoriteAsync(new RadioStation { Id = station.StationUuid, Name = station.Name, StreamUrl = station.ResolvedUrl });
    await library.FlushAsync();
    if (new RadioLibraryService(path).Favorites.Count != 1) throw new InvalidDataException("Persistence consumer failed.");
}
finally { if (File.Exists(path)) File.Delete(path); }
Console.WriteLine("Offline package consumer passed: directory, Unicode metadata and durable favorites.");

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
            Content = new StringContent("""[{"stationuuid":"fixture","name":"Synthetic radio","url_resolved":"https://fixture.example/live"}]""")
        });
}
