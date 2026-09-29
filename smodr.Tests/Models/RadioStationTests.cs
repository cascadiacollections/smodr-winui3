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
}
