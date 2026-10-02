using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class SoftwareLicenseTextTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("Short notice\r\nSecond line")]
    public void SmallNoticesArePreserved(string text) =>
        Assert.AreEqual(text, string.Concat(SoftwareLicenseText.Split(text)));

    [TestMethod]
    [DataRow("\r\n")]
    [DataRow("🎵")]
    public void SectionsDoNotSplitCrLfOrUnicodeSurrogates(string boundary)
    {
        var text = new string('a', SoftwareLicenseText.MaxSectionLength - 1) + boundary + new string('b', 10_000);
        var sections = SoftwareLicenseText.Split(text);
        Assert.AreEqual(text, string.Concat(sections));
        foreach (var section in sections)
        {
            Assert.IsTrue(section.Length is > 0 and <= SoftwareLicenseText.MaxSectionLength);
            Assert.IsFalse(char.IsHighSurrogate(section[^1]) || section[^1] == '\r');
            Assert.IsFalse(char.IsLowSurrogate(section[0]) || section[0] == '\n');
        }
    }

    [TestMethod]
    public void CompleteBundledInventoryRemainsAccessibleAsBoundedTextRuns()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "SoftwareLicenses.txt"));
        var sections = SoftwareLicenseText.Split(text);
        Assert.AreEqual(text, string.Concat(sections));
        Assert.IsTrue(sections.Count > 1);
        Assert.IsTrue(sections.All(section => section.Length is > 0 and <= SoftwareLicenseText.MaxSectionLength));
    }
}
