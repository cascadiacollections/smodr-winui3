using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using smodr.Models;
using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("TransportStress")]
public sealed class TransportStressTests
{
    [TestMethod]
    public async Task ShutdownCancelsStalledBodiesAndQueuedArtworkBeforeReleasingSemaphore()
    {
        var bodies = new ConcurrentBag<ControlledHttpBody>();
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new ControlledHttpHandler((_, _) =>
        {
            var body = new ControlledHttpBody([], stalled: true);
            bodies.Add(body); // The response owns the stream.
            if (Interlocked.Increment(ref calls) == 4) active.TrySetResult();
            return Task.FromResult(Response(body, "image/png"));
        });
        using var client = new HttpClient(handler);
        await using var loader = new StationArtworkLoader(client);
        var requests = Enumerable.Range(0, 24).Select(index => loader.GetAsync(new Uri($"https://art.example/{index}"))).ToArray();
        await active.Task.WaitAsync(TimeSpan.FromSeconds(3));
        foreach (var body in bodies) await body.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var shutdown = loader.ShutdownAsync();
        Assert.AreSame(shutdown, loader.ShutdownAsync());
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        var results = await Task.WhenAll(requests);
        Assert.IsTrue(results.All(result => result is null));
        Assert.AreEqual(4, calls);
        Assert.IsTrue(bodies.All(body => body.IsDisposed));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loader.GetAsync(new Uri("https://art.example/late")));
        // A shared service must not dispose its caller-owned client.
        using var response = await client.GetAsync(new Uri("https://art.example/client-still-owned"), HttpCompletionOption.ResponseHeadersRead);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RedirectCycleIsBoundedAndEveryResponseBodyIsDisposed()
    {
        var bodies = new List<ControlledHttpBody>();
        using var handler = new ControlledHttpHandler((request, _) =>
        {
            var body = new ControlledHttpBody([1]);
            bodies.Add(body);
            var response = Response(body, "image/png");
            response.StatusCode = HttpStatusCode.Found;
            response.Headers.Location = new Uri(request.RequestUri!.AbsolutePath == "/a" ? "/b" : "/a", UriKind.Relative);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await using var loader = new StationArtworkLoader(client);
        Assert.IsNull(await loader.GetAsync(new Uri("https://art.example/a")));
        Assert.HasCount(4, bodies);
        Assert.IsTrue(bodies.All(body => body.IsDisposed));
    }

    [TestMethod]
    public async Task TruncatedArtworkIsNeverCachedAndCanBeRetried()
    {
        var calls = 0;
        using var handler = new ControlledHttpHandler((_, _) =>
        {
            var response = Response(new ControlledHttpBody([1, 2, 3]), "image/png");
            response.Content.Headers.ContentLength = ++calls == 1 ? 9 : 3;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await using var loader = new StationArtworkLoader(client);
        var uri = new Uri("https://art.example/truncated");
        Assert.IsNull(await loader.GetAsync(uri));
        Assert.IsNotNull(await loader.GetAsync(uri));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task ValidJsonWithTruncatedAdvertisedBodyDoesNotCacheCatalogMiss()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"results\":[]}");
        var calls = 0;
        using var handler = new ControlledHttpHandler((_, _) =>
        {
            var response = Response(new ControlledHttpBody(bytes), "application/json");
            response.Content.Headers.ContentLength = ++calls == 1 ? bytes.Length + 10 : bytes.Length;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await using var lookup = new AlbumArtworkLookup(client);
        var track = new RadioTrackInfo("Synthetic", "Artist");
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.IsNull(await lookup.FindAsync(track));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task AlbumDeadlineIncludesStalledBodyAndDisposesResponse()
    {
        using var body = new ControlledHttpBody([], stalled: true);
        using var handler = new ControlledHttpHandler((_, _) => Task.FromResult(Response(body, "application/json")));
        using var client = new HttpClient(handler);
        await using var lookup = new AlbumArtworkLookup(client, TimeSpan.FromMilliseconds(100));
        var pending = lookup.FindAsync(new RadioTrackInfo("Synthetic", "Artist"));
        await body.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(body.IsDisposed);
    }

    private static HttpResponseMessage Response(ControlledHttpBody body, string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }
}
