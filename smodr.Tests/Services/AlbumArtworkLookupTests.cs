using System.Net;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class AlbumArtworkLookupTests
{
    [TestMethod]
    public async Task ReturnsResizedAppleArtworkAndCachesMatch()
    {
        var requests = 0;
        using var handler = new Handler(request =>
        {
            requests++;
            StringAssert.Contains(request.RequestUri!.Query, "media=music", StringComparison.Ordinal);
            return Json("""{"results":[{"artistName":"Artist","trackName":"Song","artworkUrl100":"https://is1-ssl.mzstatic.com/image/thumb/100x100bb.jpg","trackViewUrl":"https://music.apple.com/us/album/song/123"}]}""");
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var lookup = new AlbumArtworkLookup(client);
        var track = new RadioTrackInfo("Song", "Artist");

        var first = await lookup.FindAsync(track);
        var second = await lookup.FindAsync(track);

        Assert.AreEqual("https://is1-ssl.mzstatic.com/image/thumb/600x600bb.jpg", first?.ArtworkUrl.AbsoluteUri);
        Assert.AreEqual("https://music.apple.com/us/album/song/123", first?.StoreUrl.AbsoluteUri);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task MissingArtistAndDefinitiveMissAvoidRepeatedRequests()
    {
        var requests = 0;
        using var handler = new Handler(_ => { requests++; return Json("""{"results":[]}"""); });
        using var client = new HttpClient(handler, disposeHandler: false);
        var lookup = new AlbumArtworkLookup(client);
        Assert.IsNull(await lookup.FindAsync(new RadioTrackInfo("Song", null)));
        Assert.IsNull(await lookup.FindAsync(new RadioTrackInfo("Song", "Artist")));
        Assert.IsNull(await lookup.FindAsync(new RadioTrackInfo("Song", "Artist")));
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task RejectsUntrustedArtworkHostAndRetriesTransportFailure()
    {
        var requests = 0;
        using var handler = new Handler(_ =>
        {
            requests++;
            return requests == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json("""{"results":[{"artistName":"Artist","trackName":"Song","artworkUrl100":"https://example.com/art.jpg","trackViewUrl":"https://music.apple.com/us/album/song/123"}]}""");
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var lookup = new AlbumArtworkLookup(client);
        var track = new RadioTrackInfo("Song", "Artist");
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.AreEqual(2, requests);
    }

    [TestMethod]
    public async Task SkipsWrongFirstResultAndFindsExactSong()
    {
        using var handler = new Handler(_ => Json("""{"results":[{"artistName":"Other","trackName":"Song","artworkUrl100":"https://is1-ssl.mzstatic.com/wrong.jpg","trackViewUrl":"https://music.apple.com/wrong"},{"artistName":"Artist","trackName":"Song!","artworkUrl100":"https://is1-ssl.mzstatic.com/right/100x100bb.jpg","trackViewUrl":"https://music.apple.com/right"}]}"""));
        using var client = new HttpClient(handler, disposeHandler: false);
        var match = await new AlbumArtworkLookup(client).FindAsync(new RadioTrackInfo("Song", "Artist"));
        Assert.AreEqual("https://is1-ssl.mzstatic.com/right/600x600bb.jpg", match?.ArtworkUrl.AbsoluteUri);
    }

    [TestMethod]
    public async Task ConcurrentListenersShareRequestEvenIfOneCancels()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var handler = new AsyncHandler(() =>
        {
            Interlocked.Increment(ref requests);
            return response.Task;
        });
        using var client = new HttpClient(handler, disposeHandler: false);
        var lookup = new AlbumArtworkLookup(client);
        var track = new RadioTrackInfo("Song", "Artist");
        using var cancellation = new CancellationTokenSource();
        var first = lookup.FindAsync(track, cancellation.Token);
        var second = lookup.FindAsync(track);
        await cancellation.CancelAsync();
        using var result = Json("""{"results":[{"artistName":"Artist","trackName":"Song","artworkUrl100":"https://is1-ssl.mzstatic.com/art.jpg","trackViewUrl":"https://music.apple.com/song"}]}""");
        response.SetResult(result);

        Assert.IsNull(await first);
        Assert.IsNotNull(await second);
        Assert.AreEqual(1, requests);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class AsyncHandler(Func<Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => respond();
    }
}
