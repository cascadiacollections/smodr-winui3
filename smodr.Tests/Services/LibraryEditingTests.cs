using smodr.Models;
using smodr.Services;
using smodr.ViewModels;
using Windows.Media.Playback;

namespace smodr.Tests.Services;

[TestClass]
public sealed class LibraryEditingTests
{
    [TestMethod]
    public async Task RemoveReturnsPositionAndRestoreReinsertsThere()
    {
        using var file = new TempFile();
        var library = await LibraryWithAsync(file, "a", "b", "c");

        Assert.AreEqual(1, await library.RemoveFavoriteAsync(Station("b")));
        Assert.AreEqual(-1, await library.RemoveFavoriteAsync(Station("b")));
        await library.RestoreFavoriteAsync(Station("b"), 1);
        await library.RestoreFavoriteAsync(Station("b"), 0); // Already saved: no duplicate, no move.

        Assert.AreEqual("a,b,c", Ids(new RadioLibraryService(file.Path).Favorites));
    }

    [TestMethod]
    public async Task RestoreClampsAnIndexBeyondTheShrunkenList()
    {
        using var file = new TempFile();
        var library = await LibraryWithAsync(file, "a", "b", "c");
        var index = await library.RemoveFavoriteAsync(Station("c"));
        await library.RemoveFavoriteAsync(Station("b"));

        await library.RestoreFavoriteAsync(Station("c"), index);

        Assert.AreEqual("a,c", Ids(library.Favorites));
    }

    [TestMethod]
    public async Task ReorderPersistsAndKeepsFavoritesTheSnapshotDidNotKnow()
    {
        using var file = new TempFile();
        var library = await LibraryWithAsync(file, "a", "b", "c");
        RadioStation[] staleOrder = [Station("c"), Station("gone"), Station("a")];
        await library.ToggleFavoriteAsync(Station("d")); // Added while the drag was in flight.

        await library.ReorderFavoritesAsync(staleOrder);

        // Unknown "gone" is not resurrected; unlisted "b" and "d" keep their relative order at the end.
        Assert.AreEqual("c,a,b,d", Ids(new RadioLibraryService(file.Path).Favorites));
    }

    [TestMethod]
    public async Task ClearRecentsKeepsFavorites()
    {
        using var file = new TempFile();
        var library = await LibraryWithAsync(file, "a");
        await library.LogRecentAsync(Station("a"));
        await library.LogRecentAsync(Station("b"));

        await library.ClearRecentsAsync();

        var reopened = new RadioLibraryService(file.Path);
        Assert.IsEmpty(reopened.Recents);
        Assert.AreEqual("a", Ids(reopened.Favorites));
    }

    [TestMethod]
    public async Task ClearHistorySurvivesRestartAndLateArtworkIsIgnored()
    {
        using var file = new TempFile();
        var history = new TrackHistoryService(file.Path);
        var id = await history.RecordAsync(Station("a"), new RadioTrackInfo("Song", "Artist"));

        await history.ClearAsync();
        await history.UpdateArtworkAsync(id, new AlbumArtworkMatch(new Uri("https://is1-ssl.mzstatic.com/a.jpg"), null));
        await history.RecordAsync(Station("a"), new RadioTrackInfo("Next", "Artist"));

        var reopened = new TrackHistoryService(file.Path);
        Assert.HasCount(1, reopened.Entries);
        Assert.AreEqual("Next", reopened.Entries[0].Title);
    }

    [TestMethod]
    public async Task ClearingUnreadableHistoryFailsWithoutReplacingTheFile()
    {
        using var file = new TempFile();
        await File.WriteAllTextAsync(file.Path, "{damaged");
        var history = new TrackHistoryService(file.Path);

        await Assert.ThrowsExactlyAsync<IOException>(history.ClearAsync);

        Assert.AreEqual("{damaged", await File.ReadAllTextAsync(file.Path));
    }

    [TestMethod]
    public async Task ViewModelRemovalOffersUndoThatRestoresPosition()
    {
        using var file = new TempFile();
        using var viewModel = CreateViewModel(await LibraryWithAsync(file, "a", "b", "c"));

        await viewModel.ToggleFavoriteAsync(Station("b"));
        Assert.AreEqual("b", viewModel.RemovedFavorite?.Id);
        Assert.AreEqual("a,c", Ids(viewModel.Favorites));

        await viewModel.UndoRemoveFavoriteAsync();
        Assert.IsNull(viewModel.RemovedFavorite);
        Assert.AreEqual("a,b,c", Ids(viewModel.Favorites));
    }

    [TestMethod]
    public async Task ReaddingTheRemovedStationRetiresItsUndo()
    {
        using var file = new TempFile();
        using var viewModel = CreateViewModel(await LibraryWithAsync(file, "a"));

        await viewModel.ToggleFavoriteAsync(Station("a"));
        await viewModel.ToggleFavoriteAsync(Station("a"));

        Assert.IsNull(viewModel.RemovedFavorite);
        Assert.HasCount(1, viewModel.Favorites);
    }

    [TestMethod]
    public async Task KeyboardMoveIsClampedAndPersisted()
    {
        using var file = new TempFile();
        using var viewModel = CreateViewModel(await LibraryWithAsync(file, "a", "b", "c"));

        await viewModel.MoveFavoriteAsync(viewModel.Favorites[2], -1);
        await viewModel.MoveFavoriteAsync(viewModel.Favorites[0], -1); // Already first: no-op.

        Assert.AreEqual("a,c,b", Ids(viewModel.Favorites));
        Assert.AreEqual("a,c,b", Ids(new RadioLibraryService(file.Path).Favorites));
    }

    [TestMethod]
    public async Task ViewModelClearsHeardTracksAndTopTracks()
    {
        using var file = new TempFile();
        using var historyFile = new TempFile();
        var history = new TrackHistoryService(historyFile.Path);
        await history.RecordAsync(Station("a"), new RadioTrackInfo("Song", "Artist"));
        using var viewModel = CreateViewModel(new RadioLibraryService(file.Path), history);
        Assert.HasCount(1, viewModel.HeardTracks);

        await viewModel.ClearHeardTracksAsync();

        Assert.IsEmpty(viewModel.HeardTracks);
        Assert.IsEmpty(viewModel.TopTracks);
        Assert.AreNotEqual("Listening history could not be saved.", viewModel.Status);
    }

    private static async Task<RadioLibraryService> LibraryWithAsync(TempFile file, params string[] favoriteIds)
    {
        var library = new RadioLibraryService(file.Path);
        foreach (var id in favoriteIds) await library.ToggleFavoriteAsync(Station(id));
        return library;
    }

    private static RadioMainViewModel CreateViewModel(IRadioLibraryService library, ITrackHistoryService? history = null) =>
        new(new NullPlayer(), new NullDirectory(), library, action => action(), trackHistory: history);

    private static RadioStation Station(string id) =>
        new() { Id = id, Name = id.ToUpperInvariant(), StreamUrl = $"https://stream.example/{id}" };

    private static string Ids(IEnumerable<RadioStation> stations) => string.Join(",", stations.Select(station => station.Id));

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shoutkit-edit-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }

    private sealed class NullDirectory : IRadioDirectoryService
    {
        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>([]);
        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>([]);
        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>([]);
    }

    private sealed class NullPlayer : IRadioPlayer
    {
        public RadioStation? CurrentStation => null;
        public RadioTrackInfo? CurrentTrack => null;
        public bool IsPlaybackRequested => false;
#pragma warning disable CS0067 // Library editing never raises player events.
        public event EventHandler<RadioStation?>? StationChanged;
        public event EventHandler<RadioTrackUpdate?>? TrackChanged;
        public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
        public event EventHandler<string>? PlaybackFailed;
        public event EventHandler? UserPlaybackStarted;
#pragma warning restore CS0067
        public Task PlayStationAsync(RadioStation station) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void StopStation() { }
        public void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl) { }
    }
}
