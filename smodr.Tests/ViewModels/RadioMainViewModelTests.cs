using smodr.Models;
using smodr.Services;
using smodr.ViewModels;
using Windows.Media.Playback;

namespace smodr.Tests.ViewModels;

[TestClass]
public sealed class RadioMainViewModelTests
{
    [TestMethod]
    public async Task OlderSearchCannotReplaceNewerResults()
    {
        var firstResult = new TaskCompletionSource<IReadOnlyList<RadioStation>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = new StubDirectory((query, _) => query == "first"
            ? firstResult.Task
            : Task.FromResult<IReadOnlyList<RadioStation>>((RadioStation[])[new RadioStation { Name = "Second" }]));
        using var viewModel = new RadioMainViewModel(new StubPlayer(), directory, new StubLibrary(), action => action());

        var first = viewModel.SearchAsync("first");
        await viewModel.SearchAsync("second");
        firstResult.SetResult((RadioStation[])[new RadioStation { Name = "First" }]);
        await first;

        Assert.HasCount(1, viewModel.SearchResults);
        Assert.AreEqual("Second", viewModel.SearchResults[0].Name);
        Assert.IsFalse(viewModel.IsLoading);
    }

    private sealed class StubDirectory(
        Func<string, CancellationToken, Task<IReadOnlyList<RadioStation>>> search) : IRadioDirectoryService
    {
        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(
            int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>([]);

        public Task<IReadOnlyList<RadioStation>> SearchAsync(
            string query, int limit = 50, CancellationToken cancellationToken = default) =>
            search(query, cancellationToken);

        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(
            string genre, int limit = 50, CancellationToken cancellationToken = default) =>
            search(genre, cancellationToken);
    }

    private sealed class StubLibrary : IRadioLibraryService
    {
        public IReadOnlyList<RadioStation> Favorites => [];
        public IReadOnlyList<RadioStation> Recents => [];
        public bool IsFavorite(RadioStation station) => false;
        public void ToggleFavorite(RadioStation station) { }
        public void LogRecent(RadioStation station) { }
    }

    private sealed class StubPlayer : IRadioPlayer
    {
        public RadioStation? CurrentStation => null;
        public bool IsPlaying => false;
        public event EventHandler<RadioStation?>? StationChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<MediaPlaybackState>? PlaybackStateChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<string>? PlaybackFailed
        {
            add { }
            remove { }
        }
        public Task PlayStationAsync(RadioStation station) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void StopStation() { }
    }
}
