using smodr.Models;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioLibraryServiceTests
{
    private static readonly string[] _expectedRecentIds = ["first", "second"];

    [TestMethod]
    public async Task FavoritesAndRecentsSurviveRestart()
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
            await library.ToggleFavoriteAsync(firstStation);
            await library.LogRecentAsync(firstStation);
            await library.LogRecentAsync(secondStation);
            await library.LogRecentAsync(firstStation);

            var reopened = new RadioLibraryService(filePath);
            Assert.IsTrue(reopened.IsFavorite(firstStation));
            CollectionAssert.AreEqual(
                _expectedRecentIds,
                reopened.Recents.Select(station => station.Id).ToArray());

            await reopened.ToggleFavoriteAsync(firstStation);
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
    public async Task InvalidSavedLibraryStartsEmpty()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(filePath, "{invalid json");

            var library = new RadioLibraryService(filePath);
            await library.FlushAsync();

            Assert.IsEmpty(library.Favorites);
            Assert.IsEmpty(library.Recents);
            Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileName(filePath)}.corrupt-*"));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileName(filePath)}.corrupt-*"))
            {
                File.Delete(backup);
            }
        }
    }

    [TestMethod]
    public async Task CorruptLibraryIsBackedUpBeforeFirstNewSave()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        const string invalidContent = "{invalid json";
        try
        {
            await File.WriteAllTextAsync(filePath, invalidContent);
            var library = new RadioLibraryService(filePath);

            await library.ToggleFavoriteAsync(new RadioStation
            {
                Id = "new",
                StreamUrl = "https://example.com/live"
            });

            var backup = Directory.GetFiles(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileName(filePath)}.corrupt-*").Single();
            Assert.AreEqual(invalidContent, await File.ReadAllTextAsync(backup));
            Assert.HasCount(1, new RadioLibraryService(filePath).Favorites);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileName(filePath)}.corrupt-*"))
            {
                File.Delete(backup);
            }
        }
    }

    [TestMethod]
    public async Task FailedSaveRollsBackFavoriteChange()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}");
        var filePath = Path.Combine(root, "library.json");
        Directory.CreateDirectory(filePath);
        try
        {
            var library = new RadioLibraryService(filePath);
            var station = new RadioStation { Id = "one", StreamUrl = "https://example.com/live" };

            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
                () => library.ToggleFavoriteAsync(station));
            Assert.IsFalse(library.IsFavorite(station));

            Directory.Delete(filePath);
            await library.ToggleFavoriteAsync(station);
            Assert.IsTrue(new RadioLibraryService(filePath).IsFavorite(station));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            else if (Directory.Exists(filePath))
            {
                Directory.Delete(filePath);
            }
            Directory.Delete(root);
        }
    }

    [TestMethod]
    public async Task BusyLibraryFileIsRetried()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        var firstStation = new RadioStation { Id = "first", StreamUrl = "https://example.com/first" };
        var secondStation = new RadioStation { Id = "second", StreamUrl = "https://example.com/second" };
        try
        {
            var library = new RadioLibraryService(filePath);
            await library.LogRecentAsync(firstStation);
            await using (var hold = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var release = Task.Run(async () =>
                {
                    await Task.Delay(60);
                    await hold.DisposeAsync();
                });

                try
                {
                    var save = library.LogRecentAsync(secondStation);
                    var flush = library.FlushAsync();
                    Assert.IsFalse(save.IsCompleted);
                    Assert.IsFalse(flush.IsCompleted);
                    await save;
                    await flush;
                }
                finally
                {
                    await release;
                }
            }

            Assert.AreEqual("second", new RadioLibraryService(filePath).Recents[0].Id);
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
    public async Task NewerLibraryFormatIsPreservedReadOnly()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        var futureData = """{"SchemaVersion":999,"Favorites":[],"Recents":[]}""";
        try
        {
            await File.WriteAllTextAsync(filePath, futureData);
            var library = new RadioLibraryService(filePath);

            await Assert.ThrowsExactlyAsync<IOException>(
                () => library.LogRecentAsync(new RadioStation()));
            Assert.AreEqual(futureData, await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task ConcurrentFavoritesAndRecentsSurviveRestart()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        var stations = Enumerable.Range(0, 12)
            .Select(index => new RadioStation
            {
                Id = $"station-{index}",
                Name = $"Station {index}",
                StreamUrl = $"https://example.com/{index}"
            })
            .ToArray();
        try
        {
            var library = new RadioLibraryService(filePath);
            var operations = stations.SelectMany(station => new[]
            {
                library.ToggleFavoriteAsync(station),
                library.LogRecentAsync(station)
            });
            await Task.WhenAll(operations);

            var reopened = new RadioLibraryService(filePath);
            Assert.HasCount(stations.Length, reopened.Favorites);
            Assert.HasCount(stations.Length, reopened.Recents);
            foreach (var station in stations)
            {
                Assert.IsTrue(reopened.IsFavorite(station));
                Assert.IsTrue(reopened.Recents.Any(recent => recent.Id == station.Id));
            }
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
    public async Task ConcurrentTogglesOfSameFavoriteDoNotLoseUpdates()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"shoutkit-library-{Guid.NewGuid():N}.json");
        var station = new RadioStation { Id = "same", StreamUrl = "https://example.com/live" };
        try
        {
            var library = new RadioLibraryService(filePath);
            var toggles = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => library.ToggleFavoriteAsync(station)));
            await Task.WhenAll(toggles);

            Assert.IsFalse(library.IsFavorite(station));
            Assert.IsFalse(new RadioLibraryService(filePath).IsFavorite(station));
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
