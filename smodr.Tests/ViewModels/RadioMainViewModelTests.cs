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

    [TestMethod]
    public async Task NewSearchCancelsPreviousRequest()
    {
        var firstStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = new StubDirectory(async (query, token) =>
        {
            if (query == "first")
            {
                firstStarted.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return [];
        });
        using var viewModel = new RadioMainViewModel(new StubPlayer(), directory, new StubLibrary(), action => action());

        var first = viewModel.SearchAsync("first");
        var firstToken = await firstStarted.Task;
        await viewModel.SearchAsync("second");
        await first;

        Assert.IsTrue(firstToken.IsCancellationRequested);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public async Task DisposedViewModelIgnoresLatePopularResponse()
    {
        var response = new TaskCompletionSource<IReadOnlyList<RadioStation>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([]),
            _ => response.Task);
        var viewModel = new RadioMainViewModel(new StubPlayer(), directory, new StubLibrary(), action => action());

        var loading = viewModel.LoadPopularAsync();
        viewModel.Dispose();
        response.SetResult((RadioStation[])[new RadioStation { Name = "Late" }]);
        await loading;

        Assert.IsEmpty(viewModel.PopularStations);
    }

    [TestMethod]
    public async Task DisposingDuringSearchIsIdempotent()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = new StubDirectory(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        });
        var viewModel = new RadioMainViewModel(new StubPlayer(), directory, new StubLibrary(), action => action());

        var search = viewModel.SearchAsync("pending");
        await started.Task;
        viewModel.Dispose();
        viewModel.Dispose();
        await search;
        await viewModel.LoadPopularAsync();

        Assert.IsEmpty(viewModel.SearchResults);
        Assert.IsEmpty(viewModel.PopularStations);
    }

    [TestMethod]
    public async Task PlaybackFailureIsFriendlyAndDoesNotSaveRecentStation()
    {
        var library = new StubLibrary();
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(playStation: _ => throw new InvalidOperationException("private stream URL")),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            library,
            action => action());

        await viewModel.TogglePlaybackAsync(new RadioStation { Name = "Example" });

        Assert.AreEqual("Unable to play this station. Try another station.", viewModel.Status);
        Assert.AreEqual(0, library.RecentSaves);
    }

    [TestMethod]
    public void StopFailureIsHandled()
    {
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(stopStation: () => throw new InvalidOperationException("COM failure")),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(),
            action => action());

        viewModel.Stop();

        Assert.AreEqual("Unable to play this station. Try another station.", viewModel.Status);
    }

    [TestMethod]
    public async Task ResumeFailureForCurrentStationIsHandled()
    {
        var station = new RadioStation { Id = "same", Name = "Example" };
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(play: () => throw new InvalidOperationException("COM failure")),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(),
            action => action())
        {
            CurrentStation = station
        };

        await viewModel.TogglePlaybackAsync(station);

        Assert.AreEqual("Unable to play this station. Try another station.", viewModel.Status);
    }

    private sealed class StubDirectory(
        Func<string, CancellationToken, Task<IReadOnlyList<RadioStation>>> search,
        Func<CancellationToken, Task<IReadOnlyList<RadioStation>>>? popular = null) : IRadioDirectoryService
    {
        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(
            int limit = 50, CancellationToken cancellationToken = default) =>
            popular?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<RadioStation>>([]);

        public Task<IReadOnlyList<RadioStation>> SearchAsync(
            string query, int limit = 50, CancellationToken cancellationToken = default) =>
            search(query, cancellationToken);

        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(
            string genre, int limit = 50, CancellationToken cancellationToken = default) =>
            search(genre, cancellationToken);
    }

    private sealed class StubLibrary : IRadioLibraryService
    {
        public int RecentSaves { get; private set; }
        public IReadOnlyList<RadioStation> Favorites => [];
        public IReadOnlyList<RadioStation> Recents => [];
        public bool IsFavorite(RadioStation station) => false;
        public void ToggleFavorite(RadioStation station) { }
        public void LogRecent(RadioStation station) => RecentSaves++;
    }

    private sealed class StubPlayer(
        Func<RadioStation, Task>? playStation = null,
        Action? stopStation = null,
        Action? play = null) : IRadioPlayer
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
        public Task PlayStationAsync(RadioStation station) =>
            playStation?.Invoke(station) ?? Task.CompletedTask;
        public void Play() => play?.Invoke();
        public void Pause() { }
        public void StopStation() => stopStation?.Invoke();
    }
}
