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
    public async Task PopularSnapshotAppearsBeforeNetworkCompletes()
    {
        var response = new TaskCompletionSource<IReadOnlyList<RadioStation>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new StubCache((RadioStation[])[new RadioStation { Id = "saved", Name = "Saved" }]);
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([]), _ => response.Task),
            new StubLibrary(), action => action(), cache);

        var loading = viewModel.LoadPopularAsync();
        Assert.HasCount(1, viewModel.PopularStations);
        Assert.AreEqual("Saved", viewModel.PopularStations[0].Name);
        Assert.IsFalse(loading.IsCompleted);

        response.SetResult((RadioStation[])[new RadioStation { Id = "fresh", Name = "Fresh" }]);
        await loading;
        Assert.AreEqual("Fresh", viewModel.PopularStations[0].Name);
        Assert.AreEqual(1, cache.StoreCalls);
    }

    [TestMethod]
    public async Task SavedStationsRemainVisibleWhenOffline()
    {
        var cache = new StubCache((RadioStation[])[new RadioStation { Id = "saved", Name = "Saved" }]);
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([]),
                _ => throw new HttpRequestException("offline")),
            new StubLibrary(), action => action(), cache);

        await viewModel.LoadPopularAsync();

        Assert.HasCount(1, viewModel.PopularStations);
        StringAssert.Contains(viewModel.Status, "Directory offline", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task GenreWarmupPopulatesOnlyMissingSnapshots()
    {
        var requests = 0;
        var cache = new StubCache(Array.Empty<RadioStation>());
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(),
            new StubDirectory((_, _) =>
            {
                requests++;
                return Task.FromResult<IReadOnlyList<RadioStation>>([]);
            }),
            new StubLibrary(), action => action(), cache);

        await viewModel.WarmGenresAsync();

        Assert.AreEqual(7, requests);
        Assert.AreEqual(7, cache.StoreCalls);
        Assert.IsEmpty(viewModel.SearchResults);
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

    [TestMethod]
    public async Task TogglingBufferingStationPausesInsteadOfStartingAnotherPlay()
    {
        var station = new RadioStation { Id = "same", Name = "Example" };
        var pauseCalls = 0;
        var player = new StubPlayer(
            play: () => Assert.Fail("Buffering is an active playback request."),
            playbackRequested: true,
            pause: () => pauseCalls++);
        using var viewModel = new RadioMainViewModel(
            player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(),
            action => action())
        {
            CurrentStation = station
        };

        await viewModel.TogglePlaybackAsync(station);

        Assert.AreEqual(1, pauseCalls);
    }

    [TestMethod]
    public async Task FavoriteSaveIsAwaitedAndFailureIsReported()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var library = new StubLibrary(toggleFavorite: async _ =>
        {
            await release.Task;
            throw new IOException("save denied");
        });
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            library,
            action => action());

        var saving = viewModel.ToggleFavoriteAsync(new RadioStation { Id = "one" });
        Assert.IsFalse(saving.IsCompleted);
        release.SetResult();
        await saving;

        Assert.AreEqual("Favorites could not be saved.", viewModel.Status);
    }

    [TestMethod]
    public async Task RecentSaveFailureDoesNotReportPlaybackFailure()
    {
        var library = new StubLibrary(logRecent: _ => throw new IOException("save denied"));
        using var viewModel = new RadioMainViewModel(
            new StubPlayer(),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            library,
            action => action());

        await viewModel.TogglePlaybackAsync(new RadioStation { Id = "one" });

        Assert.AreEqual("Playing, but recent stations could not be saved.", viewModel.Status);
    }

    [TestMethod]
    public async Task NewSelectionReportsOnceButResumeDoesNot()
    {
        var station = new RadioStation { Id = "bdb9fa3b-5672-4e0e-9b75-dcb19295c483", Name = "Example" };
        var reporter = new StubReporter();
        var player = new StubPlayer(playbackRequested: false);
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), playReporter: reporter,
            privacySettings: new StubPrivacy(true));

        await viewModel.TogglePlaybackAsync(station);
        viewModel.CurrentStation = station;
        await viewModel.TogglePlaybackAsync(station);
        viewModel.PlayPause();

        Assert.AreEqual(1, reporter.Count);
    }

    [TestMethod]
    public async Task RapidSecondClickBeforeUiDispatchDoesNotReportAgain()
    {
        var station = new RadioStation { Id = "bdb9fa3b-5672-4e0e-9b75-dcb19295c483" };
        var reporter = new StubReporter();
        var player = new StubPlayer(playbackRequested: true);
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), playReporter: reporter,
            privacySettings: new StubPrivacy(true));

        await viewModel.TogglePlaybackAsync(station);
        await viewModel.TogglePlaybackAsync(station);

        Assert.AreEqual(1, reporter.Count);
    }

    [TestMethod]
    public async Task OptOutAndFailedPlayNeverReport()
    {
        var station = new RadioStation { Id = "bdb9fa3b-5672-4e0e-9b75-dcb19295c483", Name = "Example" };
        var reporter = new StubReporter();
        var privacy = new StubPrivacy(false);
        using var viewModel = new RadioMainViewModel(new StubPlayer(),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), playReporter: reporter, privacySettings: privacy);

        await viewModel.TogglePlaybackAsync(station);
        Assert.AreEqual(0, reporter.Count);
        await viewModel.SetPlayReportingEnabledAsync(true);
        Assert.IsTrue(viewModel.IsPlayReportingEnabled);

        using var failingViewModel = new RadioMainViewModel(
            new StubPlayer(playStation: _ => throw new InvalidOperationException()),
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), playReporter: reporter, privacySettings: privacy);
        await failingViewModel.TogglePlaybackAsync(station);
        Assert.AreEqual(0, reporter.Count);
    }

    [TestMethod]
    public async Task SleepTimerPausesCurrentStationAfterSwitch()
    {
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new PlaybackSleepTimer(delay: (_, _) => delay.Task);
        var pauses = 0;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new StubPlayer(playbackRequested: true, pause: () =>
        {
            Interlocked.Increment(ref pauses);
            paused.TrySetResult();
        });
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), sleepTimer: timer);

        await viewModel.TogglePlaybackAsync(new RadioStation { Id = "first", Name = "First" });
        viewModel.StartSleepTimer(TimeSpan.FromMinutes(15));
        await viewModel.TogglePlaybackAsync(new RadioStation { Id = "second", Name = "Second" });
        delay.SetResult();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(1, Volatile.Read(ref pauses));
        Assert.AreEqual("Second", player.CurrentStation?.Name);
        Assert.IsNull(viewModel.SleepTimerEndsAt);
    }

    [TestMethod]
    public async Task CancelledSleepTimerCannotPausePlayback()
    {
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new PlaybackSleepTimer(delay: (_, _) => delay.Task);
        var pauses = 0;
        var player = new StubPlayer(playbackRequested: true, pause: () => Interlocked.Increment(ref pauses));
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), sleepTimer: timer);

        await viewModel.TogglePlaybackAsync(new RadioStation { Id = "first", Name = "First" });
        viewModel.StartSleepTimer(TimeSpan.FromMinutes(15));
        viewModel.CancelSleepTimer();
        delay.SetResult();
        await Task.Delay(30);

        Assert.AreEqual(0, Volatile.Read(ref pauses));
        Assert.IsNull(viewModel.SleepTimerEndsAt);
    }

    [TestMethod]
    public async Task SleepTimerExpiryAfterStopDoesNotResumeOrPause()
    {
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timer = new PlaybackSleepTimer(delay: (_, _) => delay.Task);
        var pauses = 0;
        var player = new StubPlayer(playbackRequested: true, pause: () => Interlocked.Increment(ref pauses));
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), sleepTimer: timer);

        await viewModel.TogglePlaybackAsync(new RadioStation { Id = "first", Name = "First" });
        viewModel.StartSleepTimer(TimeSpan.FromMinutes(15));
        viewModel.Stop();
        delay.SetResult();
        await Task.Delay(30);

        Assert.AreEqual(0, Volatile.Read(ref pauses));
        Assert.IsNull(player.CurrentStation);
        Assert.IsNull(viewModel.SleepTimerEndsAt);
    }

    [TestMethod]
    public async Task AcceptedTrackAppearsInNowPlayingAndHistory()
    {
        var player = new StubPlayer();
        var history = new StubTrackHistory();
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), trackHistory: history);
        var station = new RadioStation { Id = "one", Name = "Radio One", StreamUrl = "https://example.com/live" };
        await viewModel.TogglePlaybackAsync(station);
        player.EmitTrack(new RadioTrackUpdate(station, new RadioTrackInfo("Song", "Artist")));

        Assert.AreEqual("Song", viewModel.CurrentTrack?.Title);
        Assert.HasCount(1, viewModel.HeardTracks);
        Assert.AreEqual("Song", viewModel.HeardTracks[0].Title);
        Assert.HasCount(1, viewModel.TopTracks);
        Assert.AreEqual("Artist — Song", viewModel.TopTracks[0].Display);
    }

    [TestMethod]
    public async Task LateArtworkCannotReplaceCurrentTrackAndOptOutRestoresStationArt()
    {
        var station = new RadioStation
        {
            Id = "one",
            Name = "Radio One",
            StreamUrl = "https://example.com/live",
            ArtworkUrl = "https://example.com/station.jpg"
        };
        var oldResult = new TaskCompletionSource<AlbumArtworkMatch?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new StubArtwork(track => track.Title == "Old"
            ? oldResult.Task
            : Task.FromResult<AlbumArtworkMatch?>(new AlbumArtworkMatch(
                new Uri("https://is1-ssl.mzstatic.com/new.jpg"), new Uri("https://music.apple.com/new"))));
        var player = new StubPlayer();
        var history = new StubTrackHistory();
        var privacy = new StubPrivacy(false);
        using var viewModel = new RadioMainViewModel(player,
            new StubDirectory((_, _) => Task.FromResult<IReadOnlyList<RadioStation>>([])),
            new StubLibrary(), action => action(), privacySettings: privacy,
            trackHistory: history, albumArtworkLookup: lookup);
        await viewModel.TogglePlaybackAsync(station);
        player.EmitTrack(new RadioTrackUpdate(station, new RadioTrackInfo("Old", "Artist")));
        player.EmitTrack(new RadioTrackUpdate(station, new RadioTrackInfo("New", "Artist")));
        await WaitForArtworkAsync(viewModel, "https://is1-ssl.mzstatic.com/new.jpg");
        oldResult.SetResult(new AlbumArtworkMatch(new Uri("https://is1-ssl.mzstatic.com/old.jpg"),
            new Uri("https://music.apple.com/old")));
        await Task.Delay(20);
        Assert.AreEqual("https://is1-ssl.mzstatic.com/new.jpg", viewModel.CurrentArtworkUrl);
        Assert.AreEqual("https://music.apple.com/new", viewModel.CurrentAppleMusicUrl);
        Assert.AreEqual(viewModel.CurrentArtworkUrl, player.LastArtworkUrl?.AbsoluteUri);
        Assert.AreEqual(viewModel.CurrentArtworkUrl, history.Entries[0].ArtworkUrl);

        await viewModel.SetAlbumArtworkEnabledAsync(false);
        Assert.AreEqual(station.ArtworkUrl, viewModel.CurrentArtworkUrl);
        Assert.AreEqual(station.ArtworkUrl, player.LastArtworkUrl?.AbsoluteUri);
        Assert.AreEqual(string.Empty, viewModel.CurrentAppleMusicUrl);
        player.EmitTrack(new RadioTrackUpdate(station, new RadioTrackInfo("Later", "Artist")));
        Assert.AreEqual(2, lookup.Calls);
        Assert.AreEqual(station.ArtworkUrl, viewModel.CurrentArtworkUrl);
    }

    private static async Task WaitForArtworkAsync(RadioMainViewModel viewModel, string expected)
    {
        if (viewModel.CurrentArtworkUrl == expected) return;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(viewModel.CurrentArtworkUrl)
                && viewModel.CurrentArtworkUrl == expected) changed.TrySetResult();
        }
        viewModel.PropertyChanged += Handler;
        try { await changed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { viewModel.PropertyChanged -= Handler; }
    }

    private sealed class StubArtwork(Func<RadioTrackInfo, Task<AlbumArtworkMatch?>> resolve) : IAlbumArtworkLookup
    {
        public int Calls { get; private set; }
        public Task<AlbumArtworkMatch?> FindAsync(RadioTrackInfo track, CancellationToken cancellationToken = default)
        {
            Calls++;
            return resolve(track);
        }
    }

    private sealed class StubTrackHistory : ITrackHistoryService
    {
        private readonly List<HeardTrack> _entries = [];
        public IReadOnlyList<HeardTrack> Entries => _entries;
        public Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track)
        {
            var id = Guid.NewGuid();
            _entries.Insert(0, new HeardTrack
            {
                Id = id,
                StationId = station.Id,
                StationName = station.Name,
                Title = track.Title,
                Artist = track.Artist,
                HeardAt = DateTimeOffset.UtcNow
            });
            return Task.FromResult(id);
        }
        public Task FlushAsync() => Task.CompletedTask;
        public Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork)
        {
            var index = _entries.FindIndex(item => item.Id == entryId);
            if (index >= 0)
            {
                var old = _entries[index];
                _entries[index] = new HeardTrack
                {
                    Id = old.Id,
                    StationId = old.StationId,
                    StationName = old.StationName,
                    Title = old.Title,
                    Artist = old.Artist,
                    HeardAt = old.HeardAt,
                    ArtworkUrl = artwork.ArtworkUrl.AbsoluteUri,
                    AppleMusicUrl = artwork.StoreUrl?.AbsoluteUri ?? string.Empty
                };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class StubReporter : IStationPlayReporter
    {
        public int Count { get; private set; }
        public Task ReportPlayAsync(string stationId, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPrivacy(bool enabled) : IRadioPrivacySettings
    {
        private bool _enabled = enabled;
        private bool _artworkEnabled = true;
        public bool IsPlayReportingEnabled => _enabled;
        public bool IsAlbumArtworkEnabled => _artworkEnabled;
        public Task SetPlayReportingEnabledAsync(bool value)
        {
            _enabled = value;
            return Task.CompletedTask;
        }
        public Task FlushAsync() => Task.CompletedTask;
        public Task SetAlbumArtworkEnabledAsync(bool value)
        {
            _artworkEnabled = value;
            return Task.CompletedTask;
        }
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

    private sealed class StubLibrary(
        Func<RadioStation, Task>? toggleFavorite = null,
        Func<RadioStation, Task>? logRecent = null) : IRadioLibraryService
    {
        public int RecentSaves { get; private set; }
        public IReadOnlyList<RadioStation> Favorites => [];
        public IReadOnlyList<RadioStation> Recents => [];
        public bool IsFavorite(RadioStation station) => false;
        public Task ToggleFavoriteAsync(RadioStation station) =>
            toggleFavorite?.Invoke(station) ?? Task.CompletedTask;
        public Task LogRecentAsync(RadioStation station)
        {
            RecentSaves++;
            return logRecent?.Invoke(station) ?? Task.CompletedTask;
        }
        public Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class StubCache(IReadOnlyList<RadioStation> stations) : IRadioDirectorySnapshotCache
    {
        public int StoreCalls { get; private set; }
        public Task<IReadOnlyList<RadioStation>?> GetAsync(string key, TimeSpan maxAge) =>
            Task.FromResult<IReadOnlyList<RadioStation>?>(stations.Count == 0 ? null : stations);
        public Task StoreAsync(string key, IReadOnlyList<RadioStation> values)
        {
            StoreCalls++;
            return Task.CompletedTask;
        }
        public Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class StubPlayer(
        Func<RadioStation, Task>? playStation = null,
        Action? stopStation = null,
        Action? play = null,
        bool playbackRequested = false,
        Action? pause = null) : IRadioPlayer
    {
        public RadioStation? CurrentStation { get; private set; }
        public RadioTrackInfo? CurrentTrack => null;
        public Uri? LastArtworkUrl { get; private set; }
        public bool IsPlaying => false;
        public bool IsPlaybackRequested => playbackRequested;
        public event EventHandler<RadioStation?>? StationChanged;
        public event EventHandler<RadioTrackUpdate?>? TrackChanged;
        public void EmitTrack(RadioTrackUpdate update) => TrackChanged?.Invoke(this, update);
        public void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl) => LastArtworkUrl = artworkUrl;
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
        public Task PlayStationAsync(RadioStation station)
        {
            var operation = playStation?.Invoke(station) ?? Task.CompletedTask;
            CurrentStation = station;
            StationChanged?.Invoke(this, station);
            return operation;
        }
        public void Play() => play?.Invoke();
        public void Pause() => pause?.Invoke();
        public void StopStation()
        {
            stopStation?.Invoke();
            CurrentStation = null;
        }
    }
}
