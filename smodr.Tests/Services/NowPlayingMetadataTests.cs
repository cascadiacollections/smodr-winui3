using smodr.Models;
using smodr.Services;
using Windows.Media;
using Windows.Media.Core;

namespace smodr.Tests.Services;

[TestClass]
public sealed class NowPlayingMetadataTests
{
    [TestMethod]
    public void StationMetadataIsStableWithoutTrackTitle()
    {
        var metadata = NowPlayingMetadata.ForStation(new RadioStation
        {
            Name = "  Example Radio  ",
            Country = "US",
            Codec = "AAC",
            Bitrate = 128
        });

        Assert.AreEqual("Example Radio", metadata.Title);
        StringAssert.Contains(metadata.Artist, "US", StringComparison.Ordinal);
        Assert.AreEqual("Shoutkit", metadata.AlbumTitle);
    }

    [TestMethod]
    public void EmptyStationUsesHonestFallback()
    {
        var metadata = NowPlayingMetadata.ForStation(new RadioStation());

        Assert.AreEqual("Live Radio", metadata.Title);
        Assert.AreEqual("Live Radio", metadata.Artist);
    }

    [TestMethod]
    public void PlaybackItemAppliesMetadataForSystemControls()
    {
        using var source = MediaSource.CreateFromUri(new Uri("https://example.com/radio"));
        var item = AudioService.CreatePlaybackItem(source,
            new NowPlayingMetadata("Example Radio", "US · AAC", "Shoutkit"));

        var display = item.GetDisplayProperties();
        Assert.AreEqual(MediaPlaybackType.Music, display.Type);
        Assert.AreEqual("Example Radio", display.MusicProperties.Title);
        Assert.AreEqual("US · AAC", display.MusicProperties.Artist);
        Assert.AreEqual("Shoutkit", display.MusicProperties.AlbumTitle);
    }
}
