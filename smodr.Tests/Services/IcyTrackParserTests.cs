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
}
