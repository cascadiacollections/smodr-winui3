using System.Net;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class ShoutcastDirectoryTests
{
    private const string Xml =
        "<stationlist><station id='12' name='Radio One' genre='Jazz' mt='audio/mpeg' br='128'/><station id='12' name='Duplicate'/><station id='bad' name='Invalid'/></stationlist>";

    [TestMethod]
    public async Task DirectoryEscapesKeyAndQueryAndNeverPersistsTheKeyInStations()
    {
        using var handler = new Handler(request =>
        {
            Assert.AreEqual("api.shoutcast.com", request.RequestUri!.Host);
            StringAssert.Contains(request.RequestUri.Query, "k=secret%26key", StringComparison.Ordinal);
            StringAssert.Contains(request.RequestUri.Query, "search=rock%20%26%20roll", StringComparison.Ordinal);
            Assert.IsTrue(request.Headers.UserAgent.ToString()
                .StartsWith("ShoutkitWindows/", StringComparison.Ordinal));
            return Response(Xml);
        });
        using var client = new HttpClient(handler);
        var directory = new ShoutcastDirectoryService(client, "secret&key");
        var stations = await directory.SearchAsync("rock & roll");
        Assert.HasCount(1, stations);
        Assert.AreEqual("shoutcast:12", stations[0].Id);
        Assert.AreEqual("Jazz", stations[0].Tags);
        Assert.AreEqual(128, stations[0].Bitrate);
        Assert.IsFalse(stations[0].StreamUrl.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MissingKeyMakesNoNetworkRequest()
    {
        using var handler = new Handler(_ => throw new AssertFailedException("Unexpected request"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<RadioDirectoryUnavailableException>(() =>
            new ShoutcastDirectoryService(client, "").GetPopularStationsAsync());
    }

    [TestMethod]
    public async Task PlaylistResolutionUsesKeylessTuneInAndSkipsUnsafeEntries()
    {
        using var handler = new Handler(request =>
        {
            Assert.AreEqual("yp.shoutcast.com", request.RequestUri!.Host);
            Assert.AreEqual("?id=12", request.RequestUri.Query);
            return Response(
                "[playlist]\nFile1=http://localhost/private\nFile2=https://user:secret@stream.example/live\nFile3=https://stream.example/live\n");
        });
        using var client = new HttpClient(handler);
        var station = new RadioStation
        {
            Id = "shoutcast:12",
            Name = "Radio One",
            StreamUrl = "https://yp.shoutcast.com/playlist",
            Tags = "Jazz"
        };
        var resolved = await new ShoutcastDirectoryService(client, "secret").ResolveAsync(station);
        Assert.AreEqual("https://stream.example/live", resolved.StreamUrl);
        Assert.AreEqual(station.Id, resolved.Id);
        Assert.AreEqual(station.Tags, resolved.Tags);
        Assert.AreEqual("https://yp.shoutcast.com/playlist", station.StreamUrl);
    }

    [TestMethod]
    public async Task OtherProvidersPassThroughWithoutRequest()
    {
        using var handler = new Handler(_ => throw new AssertFailedException("Unexpected request"));
        using var client = new HttpClient(handler);
        var station = new RadioStation { Id = "radio-browser-station", StreamUrl = "https://stream.example/live" };
        Assert.AreSame(station, await new ShoutcastDirectoryService(client, "secret").ResolveAsync(station));
    }

    [TestMethod]
    [DataRow("<!DOCTYPE stationlist [<!ENTITY x SYSTEM 'file:///private'>]><stationlist>&x;</stationlist>")]
    [DataRow("<invalid")]
    public async Task MalformedXmlAndExternalEntitiesAreRejected(string contents)
    {
        using var handler = new Handler(_ => Response(contents));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<RadioDirectoryUnavailableException>(() =>
            new ShoutcastDirectoryService(client, "secret").GetPopularStationsAsync());
    }

    [TestMethod]
    public async Task TransportErrorsDoNotExposeApiKeysEvenInInnerExceptions()
    {
        using var handler =
            new Handler(_ => throw new HttpRequestException("https://api.shoutcast.com/?k=private-secret"));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsExactlyAsync<RadioDirectoryUnavailableException>(() =>
            new ShoutcastDirectoryService(client, "private-secret").GetPopularStationsAsync());
        Assert.IsFalse(exception.ToString().Contains("private-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OversizedResponsesAreRejectedBeforeParsing()
    {
        using var handler = new Handler(_ => Response(new string('x', (2 * 1024 * 1024) + 1)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<RadioDirectoryUnavailableException>(() =>
            new ShoutcastDirectoryService(client, "secret").GetPopularStationsAsync());
    }

    [TestMethod]
    public async Task FallbackIsUsedForEmptyOrUnavailablePrimaryButNotForCancellation()
    {
        var fallback = new Directory(() =>
            Task.FromResult<IReadOnlyList<RadioStation>>((RadioStation[])[new RadioStation { Id = "fallback" }]));
        var empty = new Directory(() => Task.FromResult<IReadOnlyList<RadioStation>>([]));
        var service = new FallbackRadioDirectory(empty, fallback);
        Assert.AreEqual("fallback", (await service.GetPopularStationsAsync())[0].Id);
        var unavailable = new Directory(() =>
            Task.FromException<IReadOnlyList<RadioStation>>(new RadioDirectoryUnavailableException("offline", null)));
        Assert.HasCount(1, await new FallbackRadioDirectory(unavailable, fallback).SearchGenreAsync("Jazz"));
        var cancelled =
            new Directory(() => Task.FromCanceled<IReadOnlyList<RadioStation>>(new CancellationToken(true)));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new FallbackRadioDirectory(cancelled, fallback).SearchAsync("anything"));
        Assert.AreEqual(2, fallback.Calls);
    }

    private static HttpResponseMessage Response(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Directory(Func<Task<IReadOnlyList<RadioStation>>> read) : IRadioDirectoryService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return read();
        }

        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return GetPopularStationsAsync(limit, cancellationToken);
        }

        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return GetPopularStationsAsync(limit, cancellationToken);
        }
    }
}
