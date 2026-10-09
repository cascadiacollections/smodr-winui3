using System.Text;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("ParserFuzz")]
public sealed class IcyTrackParserFuzzTests
{
    private const string Mutations = "\0\r\n\t\uFFFD\uD800\uDC00';,=\" -_<>é中123abc";

    private static readonly string[] _seeds =
    [
        "StreamTitle='Artist - Song';", "title=Song,artist=Artist", "text=\"Artist - Song\" length=180",
        "StreamTitle='宇多田ヒカル - First Love';", "StreamTitle='Hüsker Dü - Ice Cold Ice';",
        "StreamTitle='Artist - Track 🎵';", "TrackId=123;StreamUrl='https://station.example/live';"
    ];

    [TestMethod]
    [DataRow(8675309)]
    [DataRow(20261002)]
    public void SeededMalformedFieldsNeverThrowOrPublishDamagedUnboundedText(int seed)
    {
        var random = new Random(seed);
        for (var sample = 0; sample < 5000; sample++)
        {
            var builder = new StringBuilder(_seeds[random.Next(_seeds.Length)]);
            for (var edit = 0; edit < 1 + (sample % 12); edit++)
            {
                var offset = random.Next(builder.Length + 1);
                switch (random.Next(3))
                {
                    case 0: builder.Insert(offset, Mutations[random.Next(Mutations.Length)]); break;
                    case 1:
                        if (offset < builder.Length)
                        {
                            builder.Remove(offset, 1);
                        }

                        break;
                    default:
                        if (offset < builder.Length)
                        {
                            builder[offset] = Mutations[random.Next(Mutations.Length)];
                        }

                        break;
                }
            }

            var raw = builder.ToString();
            var track = IcyTrackParser.Parse(raw, "Synthetic Radio");
            Assert.AreEqual(track, IcyTrackParser.Parse(raw, "Synthetic Radio"),
                $"Non-deterministic seed {seed}, sample {sample}");
            var damaged = IcyTrackParser.IsDamagedSongCue(raw, "Synthetic Radio");
            if (track is null)
            {
                continue;
            }

            Assert.IsFalse(damaged, $"Accepted damaged seed {seed}, sample {sample}");
            Assert.IsTrue(track.Title.Length is > 0 and <= 300);
            Assert.IsTrue(track.Artist is null || track.Artist.Length <= 200);
            AssertDisplaySafe(track.Title);
            if (track.Artist is { } artist)
            {
                AssertDisplaySafe(artist);
            }
        }
    }

    [TestMethod]
    public void ControlsAndUnpairedSurrogatesAreRejectedButValidUnicodeSurvives()
    {
        foreach (var bad in new[] { "\uD800", "\uDC00", "\uFFFD", "\0", "\r", "\n", "\t" })
        {
            var raw = $"StreamTitle='Artist - So{bad}ng';";
            Assert.IsNull(IcyTrackParser.Parse(raw, "Synthetic Radio"));
            Assert.IsTrue(IcyTrackParser.IsDamagedSongCue(raw, "Synthetic Radio"));
        }

        Assert.AreEqual("Track 🎵", IcyTrackParser.Parse("StreamTitle='Artist - Track 🎵';", "Synthetic Radio")?.Title);
    }

    [TestMethod]
    public void InputFieldAndNestingLimitsRemainBounded()
    {
        Assert.IsNull(IcyTrackParser.Parse(new string('x', 4097), "Synthetic Radio"));
        Assert.IsNull(IcyTrackParser.Parse("Artist - " + new string('x', 301), "Synthetic Radio"));
        Assert.IsNull(IcyTrackParser.Parse(new string('x', 201) + " - Song", "Synthetic Radio"));
        var deeplyNested = "Artist - Song";
        for (var depth = 0; depth < 50; depth++)
        {
            deeplyNested = "text=" + deeplyNested;
        }

        Assert.IsNull(IcyTrackParser.Parse(deeplyNested, "Synthetic Radio"));
        Assert.IsFalse(IcyTrackParser.IsDamagedSongCue(new string('\uFFFD', 4097), "Synthetic Radio"));
    }

    private static void AssertDisplaySafe(string value)
    {
        Assert.IsFalse(value.Any(char.IsControl));
        foreach (var rune in value.EnumerateRunes())
        {
            Assert.AreNotEqual(Rune.ReplacementChar, rune);
        }
    }
}
