using System.Buffers.Binary;
using System.Text;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class HlsId3TrackParserTests
{
    [TestMethod]
    [DataRow((byte)3)]
    [DataRow((byte)4)]
    public void ParsesTitleAndArtist(byte version)
    {
        var cue = Cue(version, ("TIT2", "A Song"), ("TPE1", "The Artist"));
        var track = HlsId3TrackParser.Parse(cue, "Radio One");
        Assert.IsNotNull(track);
        Assert.AreEqual("A Song", track.Title);
        Assert.AreEqual("The Artist", track.Artist);
    }

    [TestMethod]
    public void SplitsCombinedTitleAndRejectsPromo()
    {
        var combined = HlsId3TrackParser.Parse(Cue(4, ("TIT2", "Artist - Song")), "Radio One");
        Assert.AreEqual("Song", combined?.Title);
        Assert.AreEqual("Artist", combined?.Artist);
        Assert.IsNull(HlsId3TrackParser.Parse(Cue(4, ("TIT2", "Listen live on Radio One")), "Radio One"));
    }

    [TestMethod]
    public void RejectsTruncatedAndUnsupportedTags()
    {
        var cue = Cue(4, ("TIT2", "Song"));
        Assert.IsNull(HlsId3TrackParser.Parse(cue.AsSpan(0, cue.Length - 1), "Radio One"));
        cue[5] = 0x40; // Extended header is not decoded.
        Assert.IsNull(HlsId3TrackParser.Parse(cue, "Radio One"));
    }

    private static byte[] Cue(byte version, params (string Id, string Text)[] frames)
    {
        var body = new List<byte>();
        foreach (var (id, text) in frames)
        {
            var payload = new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            body.AddRange(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            if (version == 4) WriteSynchsafe(size, payload.Length);
            else BinaryPrimitives.WriteInt32BigEndian(size, payload.Length);
            body.AddRange(size);
            body.Add(0);
            body.Add(0);
            body.AddRange(payload);
        }

        var header = new byte[10];
        "ID3"u8.CopyTo(header);
        header[3] = version;
        WriteSynchsafe(header.AsSpan(6), body.Count);
        return [.. header, .. body];
    }

    private static void WriteSynchsafe(Span<byte> bytes, int size)
    {
        bytes[0] = (byte)((size >> 21) & 0x7F);
        bytes[1] = (byte)((size >> 14) & 0x7F);
        bytes[2] = (byte)((size >> 7) & 0x7F);
        bytes[3] = (byte)(size & 0x7F);
    }
}
