using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class TopTracksAggregatorTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CountsAcrossStationsAndIgnoresCase()
    {
        var tracks = new[]
        {
            Heard("Song", "Artist", _now.AddDays(-2), "One"),
            Heard("SONG", "ARTIST", _now.AddDays(-1), "Two"),
            Heard("Other", "Band", _now, "One")
        };
        var result = TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.Week, _now);
        Assert.HasCount(2, result);
        Assert.AreEqual(2, result[0].PlayCount);
        Assert.AreEqual("SONG", result[0].Title);
        Assert.AreEqual("ARTIST", result[0].Artist);
    }

    [TestMethod]
    public void TimeframesAndLimitAreApplied()
    {
        var tracks = new[]
        {
            Heard("Old", "Band", _now.AddDays(-40)),
            Heard("Month", "Band", _now.AddDays(-15)),
            Heard("Week", "Band", _now.AddDays(-2))
        };
        Assert.HasCount(1, TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.Week, _now));
        Assert.HasCount(2, TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.Month, _now));
        Assert.HasCount(3, TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.AllTime, _now));
        Assert.HasCount(1, TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.AllTime, _now, 1));
    }

    [TestMethod]
    public void OmitsOneSidedMetadataAndSortsTiesByRecency()
    {
        var tracks = new[]
        {
            Heard("Song", null, _now),
            Heard("Older", "Band", _now.AddDays(-2)),
            Heard("Newer", "Band", _now.AddDays(-1))
        };
        var result = TopTracksAggregator.Aggregate(tracks, TopTracksTimeframe.AllTime, _now);
        Assert.HasCount(2, result);
        Assert.AreEqual("Newer", result[0].Title);
    }

    private static HeardTrack Heard(string title, string? artist, DateTimeOffset heardAt, string stationName = "Radio") => new()
    {
        StationId = stationName,
        StationName = stationName,
        Title = title,
        Artist = artist,
        HeardAt = heardAt
    };
}
