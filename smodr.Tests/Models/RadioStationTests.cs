using smodr.Models;

namespace smodr.Tests.Models;

[TestClass]
public sealed class RadioStationTests
{
    [TestMethod]
    public void Details_CombinesUsefulStationMetadata()
    {
        var station = new RadioStation
        {
            Country = "United States",
            Tags = "jazz,blues",
            Bitrate = 128,
            Codec = "MP3"
        };

        Assert.AreEqual("United States · jazz · 128 kbps MP3", station.Details);
    }

    [TestMethod]
    public void Details_OmitsEmptyMetadata()
    {
        var station = new RadioStation { Codec = "AAC" };

        Assert.AreEqual("AAC", station.Details);
    }

    [TestMethod]
    public void Details_ToleratesNullDirectoryMetadata()
    {
        var station = new RadioStation { Tags = null!, Country = null!, Codec = null! };

        Assert.AreEqual(string.Empty, station.Details);
    }

    [TestMethod]
    public void EmptyStationIdentitiesDoNotMatch()
    {
        Assert.IsFalse(RadioStationIdentity.Matches(new RadioStation(), new RadioStation()));
    }

    [TestMethod]
    public void ListItemsAnnounceReadableNamesInsteadOfTypeNames()
    {
        Assert.AreEqual("KEXP", new RadioStation { Name = "KEXP" }.ToString());
        Assert.AreEqual("Unnamed station", new RadioStation().ToString());
        var heard = new HeardTrack { Title = "Song", Artist = "Artist", StationName = "KEXP" };
        Assert.StartsWith("Artist — Song, KEXP · ", heard.ToString());
        Assert.StartsWith("Artist — Song, 2 plays", new TopTrack("Song", "Artist", 2, DateTimeOffset.UnixEpoch).ToString());
    }
}
