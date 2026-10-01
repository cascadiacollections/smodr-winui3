using smodr.Models;

namespace smodr.Tests.Models;

[TestClass]
public sealed class StationLaunchLinkTests
{
    [TestMethod]
    public void AcceptsIosStationLinkAndHonorsNoAutoPlay()
    {
        var uri = new Uri("holmdel://station?id=kexp&name=KEXP&streamURL=https%3A%2F%2Fexample.com%2Flive&artworkURL=https%3A%2F%2Fexample.com%2Fcover.png&autoPlay=0&bitrate=128");

        Assert.IsTrue(StationLaunchLink.TryParse(uri, out var link));
        Assert.IsNotNull(link);
        Assert.IsFalse(link.AutoPlay);
        Assert.AreEqual("kexp", link.Station.Id);
        Assert.AreEqual("KEXP", link.Station.Name);
        Assert.AreEqual("https://example.com/live", link.Station.StreamUrl);
        Assert.AreEqual("https://example.com/cover.png", link.Station.ArtworkUrl);
        Assert.AreEqual(128, link.Station.Bitrate);
    }

    [TestMethod]
    public void MissingIdUsesHttpsStreamAsStableIdentity()
    {
        Assert.IsTrue(StationLaunchLink.TryParse(
            new Uri("holmdel://play?streamURL=https%3A%2F%2Fexample.com%2Fradio"), out var link));

        Assert.IsNotNull(link);
        Assert.IsTrue(link.AutoPlay);
        Assert.AreEqual(link.Station.StreamUrl, link.Station.Id);
    }

    [TestMethod]
    [DataRow("holmdel://station?id=kexp")]
    [DataRow("holmdel://settings?streamURL=https%3A%2F%2Fexample.com%2Flive")]
    [DataRow("holmdel://station?streamURL=http%3A%2F%2Fexample.com%2Flive")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Flocalhost%2Flive")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Fexample.com%2Flive&streamURL=https%3A%2F%2Fother.example%2Flive")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Fexample.com%2Flive&command=delete")]
    [DataRow("holmdel://station/other?streamURL=https%3A%2F%2Fexample.com%2Flive")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Fexample.com%2Flive&artworkURL=file%3A%2F%2F%2Fcover.png")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Fexample.com%2Flive&autoPlay=perhaps")]
    [DataRow("holmdel://station?streamURL=https%3A%2F%2Fexample.com%2Flive&name=Bad%0AName")]
    public void RejectsUnsafeOrUnsupportedLink(string raw)
    {
        Assert.IsFalse(StationLaunchLink.TryParse(new Uri(raw), out _));
    }
}
