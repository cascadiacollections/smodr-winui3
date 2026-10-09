using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using smodr.Models;
using smodr.Services;
using smodr.ViewModels;
using Windows.Media.Playback;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RecoveryUxTests
{
    [TestMethod]
    public async Task ReconnectingSpansScheduledRetryUntilAudioPlaysAgain()
    {
        var clock = new FakeTimeProvider();
        var restarts = Channel.CreateUnbounded<long>();
        using var recovery = new LiveRadioRecovery(epoch => restarts.Writer.TryWrite(epoch), _ => { }, clock);
        recovery.Begin();
        Assert.IsFalse(recovery.IsReconnecting);

        recovery.Fail();
        Assert.IsTrue(recovery.IsReconnecting);
        clock.Advance(TimeSpan.FromSeconds(2));
        await restarts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        recovery.Buffering();
        Assert.IsTrue(recovery.IsReconnecting, "The rejoined stream is still reconnecting while it buffers.");

        recovery.Playing();
        Assert.IsFalse(recovery.IsReconnecting);
        recovery.Buffering();
        Assert.IsFalse(recovery.IsReconnecting, "Ordinary rebuffering after recovery is not a reconnect.");
    }

    [TestMethod]
    public void PauseAndExhaustionEndReconnecting()
    {
        var clock = new FakeTimeProvider();
        using var recovery = new LiveRadioRecovery(_ => { }, _ => { }, clock, maxRetries: 1);
        recovery.Begin();
        recovery.Fail();
        recovery.Pause();
        Assert.IsFalse(recovery.IsReconnecting);

        using var exhausting = new LiveRadioRecovery(_ => { }, _ => { }, clock, maxRetries: 0);
        exhausting.Begin();
        exhausting.Fail();
        Assert.IsFalse(exhausting.IsReconnecting);
        Assert.IsFalse(exhausting.IsRequested);
    }

    [TestMethod]
    public async Task ViewModelSaysReconnectingThenClearsOnPlayback()
    {
        var player = new RecoveringPlayer();
        using var viewModel = CreateViewModel(player);
        await player.PlayStationAsync(Station());

        player.IsReconnecting = true;
        player.Emit(MediaPlaybackState.Buffering);
        Assert.IsTrue(viewModel.IsReconnecting);
        Assert.AreEqual("Reconnecting…", viewModel.Status);

        player.IsReconnecting = false;
        player.Emit(MediaPlaybackState.Playing);
        Assert.IsFalse(viewModel.IsReconnecting);
        Assert.AreEqual(string.Empty, viewModel.Status);
    }

    [TestMethod]
    public async Task ExhaustedRecoveryOffersOneRetryThatResumesTheStation()
    {
        var player = new RecoveringPlayer();
        using var viewModel = CreateViewModel(player);
        await player.PlayStationAsync(Station());
        player.Fail("The stream stopped responding. Select Retry or Play to try again.");
        Assert.IsTrue(viewModel.CanRetry);
        Assert.IsFalse(viewModel.IsPlaying);

        viewModel.RetryPlayback();
        viewModel.RetryPlayback();

        Assert.AreEqual(1, player.PlayCalls);
        Assert.IsFalse(viewModel.CanRetry);
    }

    [TestMethod]
    public async Task RetryIsWithdrawnWhenTheListenerMovesOn()
    {
        var player = new RecoveringPlayer();
        using var viewModel = CreateViewModel(player);
        await player.PlayStationAsync(Station());
        player.Fail("failed");

        await player.PlayStationAsync(Station("other"));
        Assert.IsFalse(viewModel.CanRetry, "A new station retires the old station's Retry.");

        player.Fail("failed");
        viewModel.Stop();
        Assert.IsFalse(viewModel.CanRetry);
        viewModel.RetryPlayback();
        Assert.AreEqual(0, player.PlayCalls);
    }

    [TestMethod]
    public void FailureWithoutAStationDoesNotOfferRetry()
    {
        var player = new RecoveringPlayer();
        using var viewModel = CreateViewModel(player);
        player.Fail("This stream could not be played. Try another station.");
        Assert.IsFalse(viewModel.CanRetry);
    }

    private static RadioMainViewModel CreateViewModel(IRadioPlayer player)
    {
        return new RadioMainViewModel(player, new NullDirectory(), new NullLibrary(), action => action());
    }

    private static RadioStation Station(string id = "station")
    {
        return new RadioStation { Id = id, Name = id, StreamUrl = $"https://stream.example/{id}" };
    }

    private sealed class RecoveringPlayer : IRadioPlayer
    {
        public int PlayCalls { get; private set; }
        public RadioStation? CurrentStation { get; private set; }
        public RadioTrackInfo? CurrentTrack => null;
        public bool IsPlaybackRequested { get; private set; }
        public bool IsReconnecting { get; set; }
        public event EventHandler<RadioStation?>? StationChanged;
        public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;
        public event EventHandler<string>? PlaybackFailed;

        public Task PlayStationAsync(RadioStation station)
        {
            CurrentStation = station;
            IsPlaybackRequested = true;
            StationChanged?.Invoke(this, station);
            return Task.CompletedTask;
        }

        public void Play()
        {
            PlayCalls++;
            IsPlaybackRequested = true;
        }

        public void Pause()
        {
            IsPlaybackRequested = false;
        }

        public void StopStation()
        {
            CurrentStation = null;
            IsPlaybackRequested = false;
            StationChanged?.Invoke(this, null);
        }

        public void SetNowPlayingArtwork(RadioStation station, Uri? artworkUrl) { }

        public void Emit(MediaPlaybackState state)
        {
            PlaybackStateChanged?.Invoke(this, state);
        }

        public void Fail(string message)
        {
            IsPlaybackRequested = false;
            IsReconnecting = false;
            PlaybackFailed?.Invoke(this, message);
        }
#pragma warning disable CS0067 // Track and user-start events are not part of recovery UX.
        public event EventHandler<RadioTrackUpdate?>? TrackChanged;
        public event EventHandler? UserPlaybackStarted;
#pragma warning restore CS0067
    }

    private sealed class NullDirectory : IRadioDirectoryService
    {
        public Task<IReadOnlyList<RadioStation>> GetPopularStationsAsync(int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RadioStation>>([]);
        }

        public Task<IReadOnlyList<RadioStation>> SearchAsync(string query, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RadioStation>>([]);
        }

        public Task<IReadOnlyList<RadioStation>> SearchGenreAsync(string genre, int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RadioStation>>([]);
        }
    }

    private sealed class NullLibrary : IRadioLibraryService
    {
        public IReadOnlyList<RadioStation> Favorites => [];
        public IReadOnlyList<RadioStation> Recents => [];

        public bool IsFavorite(RadioStation station)
        {
            return false;
        }

        public Task ToggleFavoriteAsync(RadioStation station)
        {
            return Task.CompletedTask;
        }

        public Task<int> RemoveFavoriteAsync(RadioStation station)
        {
            return Task.FromResult(-1);
        }

        public Task RestoreFavoriteAsync(RadioStation station, int index)
        {
            return Task.CompletedTask;
        }

        public Task ReorderFavoritesAsync(IReadOnlyList<RadioStation> order)
        {
            return Task.CompletedTask;
        }

        public Task LogRecentAsync(RadioStation station)
        {
            return Task.CompletedTask;
        }

        public Task ClearRecentsAsync()
        {
            return Task.CompletedTask;
        }

        public Task FlushAsync()
        {
            return Task.CompletedTask;
        }
    }
}
