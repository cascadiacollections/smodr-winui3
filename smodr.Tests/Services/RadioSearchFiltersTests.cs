using System.Net;
using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioSearchFiltersTests
{
    [TestMethod]
    public void CreateNormalizesAndDropsMalformedValues()
    {
        var filters = RadioSearchFilters.Create(128, "  Smooth \t JAZZ ", " de ");
        Assert.AreEqual(128, filters.MinimumBitrate);
        Assert.AreEqual("smooth jazz", filters.Tag);
        Assert.AreEqual("DE", filters.CountryCode);
        Assert.IsTrue(filters.IsActive);

        var dropped = RadioSearchFilters.Create(0, " \u0007 ", "Germany");
        Assert.IsNull(dropped.MinimumBitrate);
        Assert.IsNull(dropped.Tag);
        Assert.IsNull(dropped.CountryCode);
        Assert.IsFalse(dropped.IsActive);
        Assert.AreEqual(RadioSearchFilters.None, dropped);

        Assert.IsNull(RadioSearchFilters.Create(-64).MinimumBitrate);
        Assert.AreEqual(RadioSearchFilters.MaxBitrate, RadioSearchFilters.Create(int.MaxValue).MinimumBitrate);
        Assert.IsNull(RadioSearchFilters.Create(countryCode: "1A").CountryCode);
        Assert.IsNull(RadioSearchFilters.Create(countryCode: "ÄB").CountryCode);
        Assert.AreEqual(RadioSearchFilters.MaxTagLength, RadioSearchFilters.Create(tag: new string('x', 500)).Tag!.Length);
    }

    [TestMethod]
    public void WithTagKeepsOtherFilters()
    {
        var filters = RadioSearchFilters.Create(192, "rock", "US").WithTag("Hip Hop");
        Assert.AreEqual(RadioSearchFilters.Create(192, "hip hop", "US"), filters);
    }

    [TestMethod]
    public void SearchOptionsCarryOnlyActiveFilters()
    {
        var options = RadioSearchFilters.Create(128, "jazz", "GB").ToSearchOptions("  BBC  ", 500);
        Assert.AreEqual("BBC", options.Name);
        Assert.AreEqual("jazz", options.Tag);
        Assert.AreEqual("GB", options.CountryCode);
        Assert.AreEqual(128, options.BitrateMin);
        Assert.IsNull(options.BitrateMax);
        Assert.AreEqual(100, options.Limit);

        var tagOnly = RadioSearchFilters.Create(tag: "news").ToSearchOptions(" ", 50);
        Assert.IsNull(tagOnly.Name);
        Assert.IsNull(tagOnly.CountryCode);
        Assert.IsNull(tagOnly.BitrateMin);
    }

    [TestMethod]
    public async Task DirectoryServiceSendsFiltersAsRadioBrowserQuery()
    {
        var requests = new List<Uri>();
        using var handler = new RecordingHandler(requests);
        using var client = new HttpClient(handler, false);
        var service = new RadioDirectoryService(client, (Uri[])[new Uri("https://directory.example/")]);
        IRadioDirectoryService directory = service;

        await directory.SearchAsync("jazz & blues", RadioSearchFilters.Create(128, "smooth jazz", "gb"));

        var request = requests.Single();
        Assert.AreEqual("/json/stations/search", request.AbsolutePath);
        var query = request.Query;
        StringAssert.Contains(query, "name=jazz%20%26%20blues", StringComparison.Ordinal);
        StringAssert.Contains(query, "tag=smooth%20jazz", StringComparison.Ordinal);
        StringAssert.Contains(query, "countrycode=GB", StringComparison.Ordinal);
        StringAssert.Contains(query, "bitrateMin=128", StringComparison.Ordinal);
        Assert.DoesNotContain("bitrateMax", query);
        StringAssert.Contains(query, "hidebroken=true", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task FilterOnlySearchOmitsNameAndInactiveFiltersUseTheExistingPath()
    {
        var requests = new List<Uri>();
        using var handler = new RecordingHandler(requests);
        using var client = new HttpClient(handler, false);
        IRadioDirectoryService directory =
            new RadioDirectoryService(client, (Uri[])[new Uri("https://directory.example/")]);

        await directory.SearchAsync(string.Empty, RadioSearchFilters.Create(countryCode: "FR"));
        await directory.SearchAsync(string.Empty, RadioSearchFilters.None);

        Assert.HasCount(2, requests);
        Assert.DoesNotContain("name=", requests[0].Query);
        StringAssert.Contains(requests[0].Query, "countrycode=FR", StringComparison.Ordinal);
        StringAssert.StartsWith(requests[1].AbsolutePath, "/json/stations/topclick/", StringComparison.Ordinal);
    }

    [TestMethod]
    public void ClientSideMatchingFollowsIosSemantics()
    {
        var filters = RadioSearchFilters.Create(128, "jazz", "US");
        Assert.IsTrue(filters.Matches(Station(bitrate: 0, tags: "Smooth Jazz", country: "")),
            "Unknown bitrate and country do not exclude a station.");
        Assert.IsTrue(filters.Matches(Station(bitrate: 320, tags: "jazz,blues", country: "us")));
        Assert.IsFalse(filters.Matches(Station(bitrate: 64, tags: "jazz", country: "US")));
        Assert.IsFalse(filters.Matches(Station(bitrate: 128, tags: "", country: "US")),
            "A tag filter requires a matching tag.");
        Assert.IsFalse(filters.Matches(Station(bitrate: 128, tags: "jazz", country: "CA")));
        Assert.IsTrue(RadioSearchFilters.None.Matches(Station(bitrate: 1, tags: "", country: "")));
    }

    [TestMethod]
    public async Task DirectoriesWithoutServerFiltersFilterTheirPageLocally()
    {
        IRadioDirectoryService directory = new UnfilteredDirectory(
        (RadioStation[])[
            Station(bitrate: 64, tags: "rock", country: "US"),
            Station(bitrate: 256, tags: "rock", country: "US", name: "Kept")
        ]);

        var filtered = await directory.SearchAsync("rock", RadioSearchFilters.Create(128));
        var unfiltered = await directory.SearchAsync("rock", RadioSearchFilters.None);

        Assert.AreEqual("Kept", filtered.Single().Name);
        Assert.HasCount(2, unfiltered);
    }

    [TestMethod]
    public async Task FallbackDirectoryForwardsFiltersToBothDirectories()
    {
        var primary = new UnfilteredDirectory([]);
        var fallback = new UnfilteredDirectory((RadioStation[])[Station(bitrate: 320, tags: "news", country: "GB", name: "News")]);
        IRadioDirectoryService directory = new FallbackRadioDirectory(primary, fallback);

        var stations = await directory.SearchAsync("news", RadioSearchFilters.Create(tag: "news"));

        Assert.AreEqual("News", stations.Single().Name);
        Assert.AreEqual(1, primary.Searches);
        Assert.AreEqual(1, fallback.Searches);
    }

    [TestMethod]
    public void CacheIdentityDistinguishesFiltersButNotCaseOrPadding()
    {
        var jazz = RadioSearchFilters.Create(128, "jazz", "US");
        Assert.AreEqual(jazz.CacheIdentity(" BBC "), jazz.CacheIdentity("bbc"));
        Assert.AreNotEqual(jazz.CacheIdentity("bbc"), RadioSearchFilters.Create(192, "jazz", "US").CacheIdentity("bbc"));
        Assert.AreNotEqual(jazz.CacheIdentity("bbc"), RadioSearchFilters.Create(128, "jazz", "GB").CacheIdentity("bbc"));
        Assert.AreNotEqual(jazz.CacheIdentity("bbc"), RadioSearchFilters.Create(128, "rock", "US").CacheIdentity("bbc"));
        Assert.AreNotEqual(RadioDirectorySnapshotCache.SearchKey(jazz.CacheIdentity("bbc")),
            RadioDirectorySnapshotCache.SearchKey("bbc"));
    }

    private static RadioStation Station(int bitrate, string tags, string country, string name = "Station")
    {
        return new RadioStation
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = name,
            StreamUrl = "https://stream.example/" + Guid.NewGuid().ToString("N"),
            Bitrate = bitrate,
            Tags = tags,
            CountryCode = country
        };
    }

    private sealed class RecordingHandler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class UnfilteredDirectory(IReadOnlyList<RadioStation> stations) : IRadioDirectoryService
    {
        public int Searches { get; private set; }

        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(stations);
        }

        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult(stations);
        }

        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(stations);
        }
    }
}
