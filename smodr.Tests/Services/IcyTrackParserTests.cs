using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class IcyTrackParserTests
{
    [TestMethod]
    [DataRow("StreamTitle='The Artist - A Song';StreamUrl='';", "A Song", "The Artist")]
    [DataRow("StreamTitle='Don't Stop - Rock 'n' Roll';", "Rock 'n' Roll", "Don't Stop")]
    [DataRow("title='A Song';artist='The Artist';", "A Song", "The Artist")]
    [DataRow("text='The Artist - A Song';", "A Song", "The Artist")]
    [DataRow("E=MC²", "E=MC²", null)]
    public void ParsesTrackVariants(string raw, string title, string? artist)
    {
        var result = IcyTrackParser.Parse(raw, "Radio One");
        Assert.IsNotNull(result);
        Assert.AreEqual(title, result.Title);
        Assert.AreEqual(artist, result.Artist);
    }

    [TestMethod]
    [DataRow("StreamTitle='Radio One';")]
    [DataRow("StreamTitle='https://example.com';")]
    [DataRow("StreamTitle='Now playing on Radio One';")]
    [DataRow("StreamTitle='Artist - Commercial break';")]
    [DataRow("StreamTitle='';")]
    [DataRow("TrackId=123;StreamUrl='http://example.com';")]
    public void RejectsNonSongCues(string raw) =>
        Assert.IsNull(IcyTrackParser.Parse(raw, "Radio One"));

    // Dialects and edge cases mirrored from Shoutkit iOS's ICYMetadataParserTests.
    [TestMethod]
    [DataRow("StreamTitle=Earth, Wind & Fire - September;", "September", "Earth, Wind & Fire")]
    [DataRow("title=Boom,artist=Tyler, The Creator", "Boom", "Tyler, The Creator")]
    [DataRow("StreamTitle=Artist - Song;StreamUrl=https://example.com/a,b", "Song", "Artist")]
    [DataRow("title=\"Boom Boom Pow\",artist=", "Boom Boom Pow", null)]
    [DataRow("artist=\"Black Eyed Peas\",title=\"Boom Boom Pow\"", "Boom Boom Pow", "Black Eyed Peas")]
    [DataRow("TrackId=8462532111,length=180,text=Journey - Don't Stop Believin",
        "Don't Stop Believin", "Journey")]
    [DataRow("text=\"Journey - Don't Stop Believin\" amgTrackId=\"123\" length=\"00:00:00\"",
        "Don't Stop Believin", "Journey")]
    [DataRow("StreamTitle=' - title=\"Boom Boom Pow\",artist=\"Black Eyed Peas\",song_spot=\"M\" MediaBaseId=\"1187579\"';StreamUrl='';",
        "Boom Boom Pow", "Black Eyed Peas")]
    [DataRow("StreamTitle=' - text=\"Journey - Don't Stop Believin\" amgTrackId=\"123\"';",
        "Don't Stop Believin", "Journey")]
    [DataRow("E=MC² - Song 2", "Song 2", "E=MC²")]
    [DataRow("StreamTitle = 'Artist - Song';StreamUrl = '';", "Song", "Artist")]
    [DataRow(" - Orphan Title", "Orphan Title", null)]
    public void ParsesIosUpstreamDialectCases(string raw, string title, string? artist)
    {
        var result = IcyTrackParser.Parse(raw, "Radio One");
        Assert.IsNotNull(result);
        Assert.AreEqual(title, result.Title);
        Assert.AreEqual(artist, result.Artist);
    }

    [TestMethod]
    [DataRow("TrackId=8462532111,length=")]
    [DataRow("TrackId=8462532111,length=,text=")]
    [DataRow("text=\"Spot Block End\" amgTrackId=\"9876543\" length=\"00:00:00\"")]
    [DataRow("text=\"SPOT BLOCK START\" adContext=\"12345\"")]
    [DataRow("StreamTitle=' - text=\"Spot Block End\" amgTrackId=\"9876543\" length=\"00:00:00\"';")]
    [DataRow("x-key=\"value\" other=\"thing\"")]
    [DataRow("StreamTitle='kexp.org';")]
    public void SuppressesIosUpstreamNonSongCases(string raw) =>
        Assert.IsNull(IcyTrackParser.Parse(raw, "Radio One"));
}
