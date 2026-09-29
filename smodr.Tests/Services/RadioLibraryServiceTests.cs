using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioLibraryServiceTests
{
    private static readonly string[] _expectedRecentIds = ["first", "second"];

    [TestMethod]
    public void FavoritesAndRecentsSurviveRestart()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        try
        {
            var firstStation = new RadioStation
            {
                Id = "first",
                Name = "First",
                StreamUrl = "https://example.com/first.mp3"
            };
            var secondStation = new RadioStation
            {
                Id = "second",
                Name = "Second",
                StreamUrl = "https://example.com/second.mp3"
            };

            var library = new RadioLibraryService(filePath);
            library.ToggleFavorite(firstStation);
            library.LogRecent(firstStation);
            library.LogRecent(secondStation);
            library.LogRecent(firstStation);

            var reopened = new RadioLibraryService(filePath);
            Assert.IsTrue(reopened.IsFavorite(firstStation));
            CollectionAssert.AreEqual(
                _expectedRecentIds,
                reopened.Recents.Select(station => station.Id).ToArray());

            reopened.ToggleFavorite(firstStation);
            Assert.IsFalse(new RadioLibraryService(filePath).IsFavorite(firstStation));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public void InvalidSavedLibraryStartsEmpty()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(filePath, "{invalid json");

            var library = new RadioLibraryService(filePath);

            Assert.IsEmpty(library.Favorites);
            Assert.IsEmpty(library.Recents);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
