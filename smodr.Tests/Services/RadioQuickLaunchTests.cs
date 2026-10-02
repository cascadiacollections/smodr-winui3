using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioQuickLaunchTests
{
    [TestMethod]
    public void PrioritizesFavoritesDeduplicatesAndBoundsShellItems()
    {
        RadioStation[] favorites = [new() { Id = "station one", Name = "Favorite", StreamUrl = "https://private.example/key" }];
        var recents = Enumerable.Range(0, 12).Select(index => new RadioStation { Id = "station" + index, Name = "Recent" + index }).ToArray();
        var items = RadioQuickLaunch.Build(favorites, (RadioStation[])[favorites[0], .. recents]);
        Assert.HasCount(8, items);
        Assert.AreEqual("Favorites", items[0].Group);
        Assert.IsTrue(RadioQuickLaunch.TryParse(items[0].Arguments, out var id));
        Assert.AreEqual("station one", id);
        Assert.IsFalse(items.Any(item => item.Arguments.Contains("private.example", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("--station-id=")]
    [DataRow("--station-id=hello --other")]
    [DataRow("--station-id=bad%0Avalue")]
    [DataRow("--url=https://example.com")]
    public void RejectsMalformedArguments(string arguments) => Assert.IsFalse(RadioQuickLaunch.TryParse(arguments, out _));

    [TestMethod]
    public void ParsesOnlyExactExecutablePrefixAndBoundedSafeStationIdentity()
    {
        Assert.IsTrue(RadioQuickLaunch.TryParse("\"C:\\Radio App\\smodr.exe\" --station-id=shoutcast%3A12",
            out var id, "C:\\Radio App\\smodr.exe"));
        Assert.AreEqual("shoutcast:12", id);
        Assert.IsFalse(RadioQuickLaunch.TryParse("\"other.exe\" --station-id=one", out _, "smodr.exe"));
        Assert.IsFalse(RadioQuickLaunch.TryParse("--station-id=" + new string('x', 257), out _));
        Assert.HasCount(0, RadioQuickLaunch.Build((RadioStation[])[new RadioStation { Id = "one", Name = "Bad\nName" }], []));
    }
}
