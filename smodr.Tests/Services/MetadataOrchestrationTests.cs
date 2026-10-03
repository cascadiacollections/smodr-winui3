using System.Collections.Concurrent;
using System.Threading.Channels;
using smodr.Models;
using smodr.Services;
using smodr.ViewModels;
using Windows.Media.Playback;

namespace smodr.Tests.Services;

[TestClass]
[TestCategory("MetadataScenario")]
public sealed class MetadataOrchestrationTests
{
    [TestMethod]
    public async Task RapidTitlesKeepUiPlayerAndDurableHistoryAligned()
    {
        await using var scenario = new Scenario();
        var session = await scenario.SelectAsync("first");
        var old = await scenario.EmitAsync(session, "Old");
        var latest = await scenario.EmitAsync(session, "Latest");
        latest.Result.SetResult(Match("latest"));
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentArtworkUrl == Match("latest").ArtworkUrl.AbsoluteUri);
        old.Result.SetResult(Match("old"));
        await scenario.ViewModel.ShutdownAsync();
        scenario.Drain();
        Assert.AreEqual("Latest", scenario.ViewModel.CurrentTrack?.Title);
        Assert.AreEqual(Match("latest").ArtworkUrl, scenario.Player.Artwork);
        var saved = scenario.ReloadHistory();
        Assert.HasCount(2, saved);
        Assert.AreEqual("Latest", saved[0].Title);
        Assert.AreEqual(Match("latest").ArtworkUrl.AbsoluteUri, saved[0].ArtworkUrl);
        Assert.AreEqual(string.Empty, saved[1].ArtworkUrl);
    }

    [TestMethod]
    public async Task StationSwitchSuppressesRetiredMetadataAndCatalogResults()
    {
        await using var scenario = new Scenario();
        var retired = await scenario.SelectAsync("first");
        var old = await scenario.EmitAsync(retired, "Old");
        var current = await scenario.SelectAsync("second");
        var latest = await scenario.EmitAsync(current, "Latest");
        retired("StreamTitle='Artist - Retired';");
        old.Result.SetResult(Match("old"));
        latest.Result.SetResult(Match("latest"));
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentArtworkUrl == Match("latest").ArtworkUrl.AbsoluteUri);
        await scenario.ViewModel.ShutdownAsync();
        scenario.Drain();
        Assert.AreEqual("second", scenario.ViewModel.CurrentStation?.Id);
        Assert.AreEqual("Latest", scenario.ViewModel.CurrentTrack?.Title);
        Assert.AreEqual(Match("latest").ArtworkUrl, scenario.Player.Artwork);
        Assert.IsFalse(scenario.ReloadHistory().Any(entry => entry.Title == "Retired"));
    }

    [TestMethod]
    public async Task PrivacyChangeDuringLookupKeepsStationFallbackAndHistoryPrivate()
    {
        await using var scenario = new Scenario();
        var session = await scenario.SelectAsync("first");
        var pending = await scenario.EmitAsync(session, "Song");
        await scenario.ViewModel.SetAlbumArtworkEnabledAsync(false);
        scenario.Drain();
        pending.Result.SetResult(Match("song"));
        await scenario.ViewModel.ShutdownAsync();
        scenario.Drain();
        Assert.AreEqual(scenario.Player.CurrentStation!.ArtworkUrl, scenario.ViewModel.CurrentArtworkUrl);
        Assert.AreEqual(scenario.Player.CurrentStation.ArtworkUrl, scenario.Player.Artwork?.AbsoluteUri);
        Assert.AreEqual(string.Empty, scenario.ReloadHistory().Single().ArtworkUrl);
        Assert.IsFalse(scenario.Privacy.IsAlbumArtworkEnabled);
    }

    [TestMethod]
    public async Task ReconnectDeduplicatesHistoryAndDamagedCueClearsArtwork()
    {
        await using var scenario = new Scenario();
        var session = await scenario.SelectAsync("first");
        var pending = await scenario.EmitAsync(session, "Song");
        pending.Result.SetResult(Match("song"));
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentArtworkUrl == Match("song").ArtworkUrl.AbsoluteUri);
        scenario.Player.Pause();
        scenario.Player.Play();
        var reconnected = await scenario.Reader.Sessions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        var again = await scenario.EmitAsync(reconnected, "Song");
        again.Result.SetResult(Match("song"));
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentArtworkUrl == Match("song").ArtworkUrl.AbsoluteUri);
        reconnected("StreamTitle='Artist - S\uFFFDng';");
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentTrack is null);
        Assert.AreEqual(scenario.Player.CurrentStation!.ArtworkUrl, scenario.ViewModel.CurrentArtworkUrl);
        Assert.AreEqual(scenario.Player.CurrentStation.ArtworkUrl, scenario.Player.Artwork?.AbsoluteUri);
        await scenario.ViewModel.ShutdownAsync();
        scenario.Drain();
        Assert.HasCount(1, scenario.ReloadHistory());
        Assert.AreEqual("Song", scenario.ReloadHistory()[0].Title);
    }

    private static AlbumArtworkMatch Match(string name) => new(new Uri($"https://art.example/{name}.jpg"), null);

    [TestMethod]
    public async Task QueuedArtworkFromEarlierOccurrenceCannotReplaceSameSongAfterInterveningTrack()
    {
        await using var scenario = new Scenario();
        var session = await scenario.SelectAsync("first");
        var first = await scenario.EmitAsync(session, "Song A");
        await scenario.UntilAsync(() => scenario.ViewModel.HeardTracks.Count == 1);
        first.Result.SetResult(Match("earlier-a"));
        var queuedArtwork = await scenario.TakePostedAsync();
        var middle = await scenario.EmitAsync(session, "Song B");
        var repeated = await scenario.EmitAsync(session, "Song A");
        queuedArtwork();
        Assert.AreEqual(scenario.Player.CurrentStation!.ArtworkUrl, scenario.ViewModel.CurrentArtworkUrl);
        repeated.Result.SetResult(Match("current-a"));
        await scenario.UntilAsync(() => scenario.ViewModel.CurrentArtworkUrl == Match("current-a").ArtworkUrl.AbsoluteUri);
        middle.Result.SetResult(Match("late-b"));
        await scenario.ViewModel.ShutdownAsync();
        scenario.Drain();
        var saved = scenario.ReloadHistory();
        Assert.HasCount(3, saved);
        Assert.AreEqual("Song A", saved[0].Title);
        Assert.AreEqual(Match("current-a").ArtworkUrl.AbsoluteUri, saved[0].ArtworkUrl);
        Assert.AreEqual(string.Empty, saved[1].ArtworkUrl);
        Assert.AreEqual(string.Empty, saved[2].ArtworkUrl);
        Assert.AreEqual(Match("current-a").ArtworkUrl, scenario.Player.Artwork);
    }

    [TestMethod]
    public async Task ShutdownWaitsForAcceptedHistoryRecordBeforeFlushAndIgnoresQueuedUiRefresh()
    {
        await using var scenario = new Scenario(delayHistory: true);
        var session = await scenario.SelectAsync("first");
        await scenario.EmitAsync(session, "Accepted");
        await scenario.History.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var shutdown = scenario.ViewModel.ShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted);
        scenario.History.Release.TrySetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual("Accepted", scenario.ReloadHistory().Single().Title);
        scenario.Drain();
        Assert.HasCount(0, scenario.ViewModel.HeardTracks);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("shoutkit-metadata-scenario-");
        private readonly ConcurrentQueue<Action> _dispatch = new();
        private readonly Channel<bool> _posted = Channel.CreateUnbounded<bool>();
        private readonly Catalog _catalog = new();
        public Reader Reader { get; } = new();
        public Player Player { get; }
        public RadioPrivacySettings Privacy { get; }
        public RadioMainViewModel ViewModel { get; }
        public DeferredHistory History { get; }

        public Scenario(bool delayHistory = false)
        {
            Player = new Player(new IcyTrackMonitor(new Probe(), continuousReader: Reader));
            Privacy = new RadioPrivacySettings(Path.Combine(_directory.FullName, "privacy.json"));
            History = new DeferredHistory(new TrackHistoryService(Path.Combine(_directory.FullName, "history.json")));
            if (!delayHistory) History.Release.TrySetResult();
            ViewModel = new RadioMainViewModel(Player, new DirectoryStub(),
                new RadioLibraryService(Path.Combine(_directory.FullName, "library.json")),
                action => { _dispatch.Enqueue(action); _posted.Writer.TryWrite(true); },
                privacySettings: Privacy,
                trackHistory: History,
                albumArtworkLookup: _catalog, canPrefetch: () => false);
        }

        public IReadOnlyList<HeardTrack> ReloadHistory() =>
            new TrackHistoryService(Path.Combine(_directory.FullName, "history.json")).Entries;

        public async Task<Action<string>> SelectAsync(string id)
        {
            await ViewModel.PlaySavedStationAsync(new RadioStation
            {
                Id = id,
                Name = id,
                StreamUrl = $"https://stream.example/{id}",
                ArtworkUrl = $"https://art.example/{id}.png"
            });
            Drain();
            return await Reader.Sessions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }

        public async Task<Request> EmitAsync(Action<string> session, string title)
        {
            session($"StreamTitle='Artist - {title}';");
            Drain();
            return await _catalog.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }

        public void Drain() { while (_dispatch.TryDequeue(out var action)) action(); }

        public async Task<Action> TakePostedAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (true)
            {
                if (_dispatch.TryDequeue(out var action)) return action;
                await _posted.Reader.ReadAsync(deadline.Token);
            }
        }

        public async Task UntilAsync(Func<bool> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (true)
            {
                Drain();
                if (condition()) return;
                await _posted.Reader.ReadAsync(deadline.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            History.Release.TrySetResult();
            await ViewModel.ShutdownAsync();
            await Privacy.FlushAsync();
            _directory.Delete(recursive: true);
        }
    }

    private sealed class DeferredHistory(ITrackHistoryService inner) : ITrackHistoryService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<HeardTrack> Entries => inner.Entries;
        public async Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track)
        {
            Started.TrySetResult();
            await Release.Task;
            return await inner.RecordAsync(station, track);
        }
        public Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork) => inner.UpdateArtworkAsync(entryId, artwork);
        // This flush cannot see the admission blocked above: the view model must drain first.
        public Task FlushAsync() => inner.FlushAsync();
    }

    private sealed record Request(TaskCompletionSource<AlbumArtworkMatch?> Result);

    private sealed class Catalog : IAlbumArtworkLookup, IAsyncDisposable
    {
        private readonly ConcurrentBag<Request> _pending = [];
        public Channel<Request> Requests { get; } = Channel.CreateUnbounded<Request>();
        public Task<AlbumArtworkMatch?> FindAsync(RadioTrackInfo track, CancellationToken cancellationToken = default)
        {
            // Deliberately uncooperative: exercises ownership checks after a provider returns late.
            var request = new Request(new TaskCompletionSource<AlbumArtworkMatch?>(TaskCreationOptions.RunContinuationsAsynchronously));
            _pending.Add(request);
            Requests.Writer.TryWrite(request);
            return request.Result.Task;
        }
        public ValueTask DisposeAsync()
        {
            foreach (var request in _pending) request.Result.TrySetResult(null);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Reader : IContinuousTrackMetadataReader
    {
        public Channel<Action<string>> Sessions { get; } = Channel.CreateUnbounded<Action<string>>();
        public async Task<bool> ListenAsync(Uri streamUri, Action<string> onMetadata, CancellationToken cancellationToken = default)
        {
            Sessions.Writer.TryWrite(onMetadata);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }
    }

    private sealed class Probe : ITrackMetadataProbe
    {
        public Task<IcyProbeResult> ProbeAsync(Uri streamUri, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IcyProbeResult(false, null));
    }

    private sealed class DirectoryStub : IRadioDirectoryService
    {
        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>((RadioStation[])[]);
        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) => GetPopularStationsAsync(limit, cancellationToken);
        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50, CancellationToken cancellationToken = default) => GetPopularStationsAsync(limit, cancellationToken);
    }

    private sealed class Player : IRadioPlayer, IAsyncDisposable
    {
        private readonly IcyTrackMonitor _monitor;
        public Player(IcyTrackMonitor monitor)
        {
            _monitor = monitor;
            monitor.TrackChanged += (_, update) => { CurrentTrack = update.Track; TrackChanged?.Invoke(this, update); };
            monitor.TrackInvalidated += (_, station) =>
            {
                if (!ReferenceEquals(CurrentStation, station)) return;
                CurrentTrack = null;
                TrackChanged?.Invoke(this, null);
            };
        }
        public RadioStation? CurrentStation { get; private set; }
        public RadioTrackInfo? CurrentTrack { get; private set; }
        public Uri? Artwork { get; private set; }
        public bool IsPlaying => IsPlaybackRequested;
        public bool IsPlaybackRequested { get; private set; }
        public event EventHandler<RadioStation?>? StationChanged;
        public event EventHandler<RadioTrackUpdate?>? TrackChanged;
        public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
        public event EventHandler<string>? PlaybackFailed { add { } remove { } }
        public event EventHandler? UserPlaybackStarted { add { } remove { } }
        public Task PlayStationAsync(RadioStation station)
        {
            _monitor.Stop();
            CurrentStation = station;
            CurrentTrack = null;
            StationChanged?.Invoke(this, station);
            Play();
            return Task.CompletedTask;
        }
        public void Play()
        {
            IsPlaybackRequested = true;
            if (CurrentStation is { } station) _monitor.Start(station);
            PlaybackStateChanged?.Invoke(this, MediaPlaybackState.Playing);
        }
        public void Pause() { _monitor.Stop(); IsPlaybackRequested = false; }
        public void StopStation() { Pause(); CurrentStation = null; }
        public void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl)
        {
            if (ReferenceEquals(CurrentStation, station)) Artwork = artworkUrl;
        }
        public async ValueTask DisposeAsync() => await _monitor.ShutdownAsync();
    }
}
