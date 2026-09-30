using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using smodr.Models;
using smodr.Services;
using Windows.Media.Playback;

namespace smodr.ViewModels;

public partial class RadioMainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] _warmGenres =
        ["alternative", "classical", "electronic", "hip hop", "jazz", "news", "rock"];
    private readonly IRadioPlayer _audio;
    private readonly IRadioDirectoryService _directory;
    private readonly IRadioLibraryService _library;
    private readonly IRadioDirectorySnapshotCache? _cache;
    private readonly IStationPlayReporter? _playReporter;
    private readonly IRadioPrivacySettings? _privacySettings;
    private readonly PlaybackSleepTimer _sleepTimer;
    private readonly ITrackHistoryService? _trackHistory;
    private readonly IAlbumArtworkLookup? _albumArtworkLookup;
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _artworkCancellation;
    private Task<Guid?>? _currentHistoryRecord;
    private int _searchVersion;
    private bool _loadingPopular;
    private bool _loadingSearch;
    private int _disposed;

    public RadioMainViewModel(
        IRadioPlayer audio,
        IRadioDirectoryService directory,
        IRadioLibraryService library,
        Action<Action>? dispatch = null,
        IRadioDirectorySnapshotCache? cache = null,
        IStationPlayReporter? playReporter = null,
        IRadioPrivacySettings? privacySettings = null,
        PlaybackSleepTimer? sleepTimer = null,
        ITrackHistoryService? trackHistory = null,
        IAlbumArtworkLookup? albumArtworkLookup = null)
    {
        if (dispatch is null)
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            _dispatch = action => queue.TryEnqueue(() => action());
        }
        else
        {
            _dispatch = dispatch;
        }

        _audio = audio;
        _directory = directory;
        _library = library;
        _cache = cache;
        _playReporter = playReporter;
        _privacySettings = privacySettings;
        _sleepTimer = sleepTimer ?? new PlaybackSleepTimer();
        _trackHistory = trackHistory;
        _albumArtworkLookup = albumArtworkLookup;
        _sleepTimer.Elapsed += SleepTimer_Elapsed;
        _audio.StationChanged += Audio_StationChanged;
        _audio.TrackChanged += Audio_TrackChanged;
        _audio.PlaybackStateChanged += Audio_PlaybackStateChanged;
        _audio.PlaybackFailed += Audio_PlaybackFailed;
        RefreshLibraryCollections();
        if (_trackHistory is not null) RefreshTrackCollections();
    }

    public ObservableCollection<RadioStation> PopularStations { get; } = [];
    public ObservableCollection<RadioStation> SearchResults { get; } = [];
    public ObservableCollection<RadioStation> Favorites { get; } = [];
    public ObservableCollection<RadioStation> Recents { get; } = [];
    public ObservableCollection<HeardTrack> HeardTracks { get; } = [];
    public ObservableCollection<TopTrack> TopTracks { get; } = [];
    public TopTracksTimeframe SelectedTopTracksTimeframe { get; private set; } = TopTracksTimeframe.Week;

    [ObservableProperty] public partial RadioStation? CurrentStation { get; set; }
    [ObservableProperty] public partial RadioTrackInfo? CurrentTrack { get; set; }
    [ObservableProperty] public partial string CurrentArtworkUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrentAppleMusicUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Tuning in…";
    public bool IsPlayReportingEnabled => _privacySettings?.IsPlayReportingEnabled ?? false;
    public bool IsAlbumArtworkEnabled => _privacySettings?.IsAlbumArtworkEnabled ?? false;

    public Task SetPlayReportingEnabledAsync(bool enabled) =>
        _privacySettings?.SetPlayReportingEnabledAsync(enabled) ?? Task.CompletedTask;

    public Task SetAlbumArtworkEnabledAsync(bool enabled)
    {
        if (_privacySettings is null) return Task.CompletedTask;
        var save = _privacySettings.SetAlbumArtworkEnabledAsync(enabled);
        _dispatch(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (CurrentStation is { } station && CurrentTrack is { } track)
                StartArtworkLookup(station, track);
            else
            {
                CurrentArtworkUrl = CurrentStation?.ArtworkUrl ?? string.Empty;
                CurrentAppleMusicUrl = string.Empty;
                if (CurrentStation is { } current)
                    _audio.SetNowPlayingArtwork(current, ParseArtworkUrl(current.ArtworkUrl));
            }
        });
        return save;
    }

    public Task FlushPrivacySettingsAsync() =>
        _privacySettings?.FlushAsync() ?? Task.CompletedTask;

    public DateTimeOffset? SleepTimerEndsAt => _sleepTimer.EndsAt;
    public TimeSpan? SleepTimerRemaining => _sleepTimer.Remaining;

    public void StartSleepTimer(TimeSpan duration)
    {
        _sleepTimer.Start(duration);
        OnPropertyChanged(nameof(SleepTimerEndsAt));
    }

    public void CancelSleepTimer()
    {
        _sleepTimer.Cancel();
        OnPropertyChanged(nameof(SleepTimerEndsAt));
    }

    public async Task LoadPopularAsync()
    {
        if (_lifetimeCancellation.IsCancellationRequested || PopularStations.Count > 0)
        {
            return;
        }

        _loadingPopular = true;
        UpdateLoading();
        Status = "Tuning in…";
        try
        {
            var cached = await GetCachedAsync(RadioDirectorySnapshotCache.PopularKey(60), TimeSpan.FromDays(30));
            var showedCache = cached is { Count: > 0 } && !_lifetimeCancellation.IsCancellationRequested;
            if (showedCache)
            {
                Replace(PopularStations, cached!);
                Status = "Showing saved stations · Refreshing…";
            }

            var stations = await _directory.GetPopularStationsAsync(60, _lifetimeCancellation.Token);
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            Replace(PopularStations, stations);
            Status = PopularStations.Count == 0 ? "Nothing here yet" : string.Empty;
            await StoreCachedAsync(RadioDirectorySnapshotCache.PopularKey(60), stations);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                AppDiagnostics.Record("popular.load", ex);
                Status = PopularStations.Count > 0
                    ? "Showing saved stations · Directory offline"
                    : $"Directory unavailable: {ex.Message}";
            }
        }
        finally
        {
            _loadingPopular = false;
            UpdateLoading();
        }
    }

    public Task SearchAsync(string query) => SearchCoreAsync(
        token => _directory.SearchAsync(query, cancellationToken: token),
        string.IsNullOrWhiteSpace(query) ? "Browse by genre" : "Searching…",
        string.IsNullOrWhiteSpace(query)
            ? RadioDirectorySnapshotCache.PopularKey(50)
            : RadioDirectorySnapshotCache.SearchKey(query));

    public Task SearchGenreAsync(string genre) => SearchCoreAsync(
        token => _directory.SearchGenreAsync(genre, cancellationToken: token),
        $"Browsing {genre}…",
        RadioDirectorySnapshotCache.GenreKey(genre));

    public async Task WarmGenresAsync()
    {
        if (_cache is null) return;
        foreach (var genre in _warmGenres)
        {
            if (_lifetimeCancellation.IsCancellationRequested) return;
            var key = RadioDirectorySnapshotCache.GenreKey(genre);
            if (await GetCachedAsync(key, TimeSpan.FromDays(7)) is not null) continue;
            try
            {
                var stations = await _directory.SearchGenreAsync(genre, cancellationToken: _lifetimeCancellation.Token);
                await StoreCachedAsync(key, stations);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                AppDiagnostics.Record("directory.warmup", exception);
                return; // No more speculative requests if the directory is down.
            }
        }
    }

    private async Task SearchCoreAsync(
        Func<CancellationToken, Task<IReadOnlyList<RadioStation>>> search,
        string loadingStatus,
        string cacheKey)
    {
        var version = Interlocked.Increment(ref _searchVersion);
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var previousSearch = Interlocked.Exchange(ref _searchCancellation, cancellation);
        try
        {
            if (previousSearch is not null)
            {
                await previousSearch.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // An earlier search may complete and dispose its token concurrently.
        }

        if (version != Volatile.Read(ref _searchVersion) || cancellation.IsCancellationRequested)
        {
            Interlocked.CompareExchange(ref _searchCancellation, null, cancellation);
            cancellation.Dispose();
            return;
        }

        _loadingSearch = true;
        UpdateLoading();
        Status = loadingStatus;
        SearchResults.Clear();
        try
        {
            var cached = await GetCachedAsync(cacheKey, TimeSpan.FromDays(7));
            if (cached is { Count: > 0 }
                && !cancellation.IsCancellationRequested
                && version == Volatile.Read(ref _searchVersion))
            {
                Replace(SearchResults, cached);
                Status = "Showing saved stations · Refreshing…";
            }

            var stations = await search(cancellation.Token);
            if (cancellation.IsCancellationRequested || version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            Replace(SearchResults, stations);
            Status = SearchResults.Count == 0 ? "No matching stations" : string.Empty;
            await StoreCachedAsync(cacheKey, stations);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && version == Volatile.Read(ref _searchVersion))
            {
                AppDiagnostics.Record("directory.search", ex);
                Status = SearchResults.Count > 0
                    ? "Showing saved stations · Directory offline"
                    : $"Search unavailable: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _searchCancellation, null, cancellation), cancellation))
            {
                _loadingSearch = false;
                UpdateLoading();
            }

            cancellation.Dispose();
        }
    }

    public async Task TogglePlaybackAsync(RadioStation station)
    {
        try
        {
            // The player changes station synchronously, while the UI property may
            // still be waiting on the dispatcher after a fast second click.
            var activeStation = _audio.CurrentStation ?? CurrentStation;
            if (activeStation is not null && RadioStationIdentity.Matches(activeStation, station))
            {
                if (_audio.IsPlaybackRequested)
                {
                    _audio.Pause();
                }
                else
                {
                    _audio.Play();
                }

                return;
            }

            await _audio.PlayStationAsync(station);
            if (_privacySettings?.IsPlayReportingEnabled == true && _playReporter is not null)
            {
                _ = ReportPlayBestEffortAsync(station.Id);
            }
            try
            {
                await _library.LogRecentAsync(station);
                if (Volatile.Read(ref _disposed) == 0)
                {
                    RefreshLibraryCollections();
                }
            }
            catch (Exception exception)
            {
                AppDiagnostics.Record("library.recent-save", exception);
                Status = "Playing, but recent stations could not be saved.";
            }
        }
        catch (Exception ex)
        {
            ReportPlaybackFailure(ex);
        }
    }

    public void PlayPause()
    {
        try
        {
            if (_audio.IsPlaybackRequested)
            {
                _audio.Pause();
            }
            else
            {
                _audio.Play();
            }
        }
        catch (Exception ex)
        {
            ReportPlaybackFailure(ex);
        }
    }

    public void Stop()
    {
        try
        {
            _audio.StopStation();
            CurrentStation = null;
            IsPlaying = false;
        }
        catch (Exception ex)
        {
            ReportPlaybackFailure(ex);
        }
    }

    public async Task ToggleFavoriteAsync(RadioStation station)
    {
        try
        {
            await _library.ToggleFavoriteAsync(station);
            if (Volatile.Read(ref _disposed) == 0)
            {
                RefreshLibraryCollections();
                OnPropertyChanged(nameof(CurrentStation));
            }
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("library.favorite-save", exception);
            Status = "Favorites could not be saved.";
        }
    }

    public bool IsFavorite(RadioStation station) => _library.IsFavorite(station);

    public Task FlushLibraryAsync() => _library.FlushAsync();
    public Task FlushTrackHistoryAsync() => _trackHistory?.FlushAsync() ?? Task.CompletedTask;

    public void SetTopTracksTimeframe(TopTracksTimeframe timeframe)
    {
        SelectedTopTracksTimeframe = timeframe;
        RefreshTopTracks();
    }

    public Task FlushDirectoryCacheAsync() => _cache?.FlushAsync() ?? Task.CompletedTask;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _audio.StationChanged -= Audio_StationChanged;
        _audio.TrackChanged -= Audio_TrackChanged;
        _audio.PlaybackStateChanged -= Audio_PlaybackStateChanged;
        _audio.PlaybackFailed -= Audio_PlaybackFailed;
        _sleepTimer.Elapsed -= SleepTimer_Elapsed;
        _sleepTimer.Dispose();
        CancelArtworkLookup();
        _lifetimeCancellation.Cancel();
        var currentSearch = Interlocked.Exchange(ref _searchCancellation, null);
        try
        {
            currentSearch?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed search may dispose its token concurrently.
        }
        currentSearch?.Dispose();
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Audio_StationChanged(object? sender, RadioStation? station) =>
        _dispatch(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            CancelArtworkLookup();
            _currentHistoryRecord = null;
            CurrentStation = station;
            CurrentTrack = null;
            CurrentArtworkUrl = station?.ArtworkUrl ?? string.Empty;
            CurrentAppleMusicUrl = string.Empty;
        });

    private void Audio_TrackChanged(object? sender, RadioTrackUpdate? update) =>
        _dispatch(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (update is null)
            {
                CancelArtworkLookup();
                _currentHistoryRecord = null;
                CurrentTrack = null;
                CurrentArtworkUrl = CurrentStation?.ArtworkUrl ?? string.Empty;
                CurrentAppleMusicUrl = string.Empty;
                return;
            }
            if (!ReferenceEquals(_audio.CurrentStation, update.Station)) return;
            CurrentTrack = update.Track;
            _currentHistoryRecord = _trackHistory is null ? null : RecordTrackBestEffortAsync(update);
            StartArtworkLookup(update.Station, update.Track);
        });

    private void StartArtworkLookup(RadioStation station, RadioTrackInfo track)
    {
        CancelArtworkLookup();
        CurrentArtworkUrl = station.ArtworkUrl;
        CurrentAppleMusicUrl = string.Empty;
        _audio.SetNowPlayingArtwork(station, ParseArtworkUrl(station.ArtworkUrl));
        if (_albumArtworkLookup is null || _privacySettings?.IsAlbumArtworkEnabled != true
            || string.IsNullOrWhiteSpace(track.Artist)) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _artworkCancellation = cancellation;
        _ = ResolveArtworkAsync(station, track, _currentHistoryRecord, cancellation);
    }

    private async Task ResolveArtworkAsync(RadioStation station, RadioTrackInfo track,
        Task<Guid?>? historyRecord, CancellationTokenSource cancellation)
    {
        try
        {
            var match = await _albumArtworkLookup!.FindAsync(track, cancellation.Token);
            if (match is null || cancellation.IsCancellationRequested) return;
            _dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) == 0 && !cancellation.IsCancellationRequested
                    && ReferenceEquals(_audio.CurrentStation, station) && CurrentTrack == track
                    && _privacySettings?.IsAlbumArtworkEnabled == true)
                {
                    CurrentArtworkUrl = match.ArtworkUrl.AbsoluteUri;
                    CurrentAppleMusicUrl = match.StoreUrl?.AbsoluteUri ?? string.Empty;
                    _audio.SetNowPlayingArtwork(station, match.ArtworkUrl);
                    if (_trackHistory is not null && historyRecord is not null)
                        _ = UpdateHistoryArtworkBestEffortAsync(historyRecord, match);
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppDiagnostics.Record("artwork.lookup", exception); }
    }

    private void CancelArtworkLookup()
    {
        var previous = _artworkCancellation;
        _artworkCancellation = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private static Uri? ParseArtworkUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri : null;

    private async Task UpdateHistoryArtworkBestEffortAsync(Task<Guid?> historyRecord,
        AlbumArtworkMatch artwork)
    {
        try
        {
            var entryId = await historyRecord;
            if (entryId is not { } id) return;
            await _trackHistory!.UpdateArtworkAsync(id, artwork);
            _dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) == 0) RefreshTrackCollections();
            });
        }
        catch (Exception exception) { AppDiagnostics.Record("track-history.artwork", exception); }
    }

    private async Task<Guid?> RecordTrackBestEffortAsync(RadioTrackUpdate update)
    {
        try
        {
            var entryId = await _trackHistory!.RecordAsync(update.Station, update.Track);
            _dispatch(() =>
            {
                if (Volatile.Read(ref _disposed) == 0) RefreshTrackCollections();
            });
            return entryId;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("track-history.write", exception);
            return null;
        }
    }

    private void Audio_PlaybackStateChanged(object? sender, MediaPlaybackState state) =>
        _dispatch(() =>
        {
            // Buffering/reconnect still represents an active listening intent;
            // the transport button must offer Pause, not start another Play.
            IsPlaying = _audio.IsPlaybackRequested;
            Status = state switch
            {
                MediaPlaybackState.Opening => "Loading…",
                MediaPlaybackState.Buffering => "Buffering…",
                MediaPlaybackState.Paused => "Paused",
                MediaPlaybackState.Playing => string.Empty,
                _ => Status
            };
        });

    private void Audio_PlaybackFailed(object? sender, string message) =>
        _dispatch(() =>
        {
            IsPlaying = false;
            Status = $"Unable to play this station: {message}";
        });

    private void SleepTimer_Elapsed(object? sender, EventArgs args) =>
        _dispatch(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || _sleepTimer.EndsAt is not null) return;
            OnPropertyChanged(nameof(SleepTimerEndsAt));
            if (_audio.CurrentStation is null || !_audio.IsPlaybackRequested) return;
            try { _audio.Pause(); }
            catch (Exception exception) { ReportPlaybackFailure(exception); }
        });

    private void RefreshLibraryCollections()
    {
        Replace(Favorites, _library.Favorites);
        Replace(Recents, _library.Recents);
    }

    private void RefreshTrackCollections()
    {
        if (_trackHistory is null) return;
        Replace(HeardTracks, _trackHistory.Entries);
        RefreshTopTracks();
    }

    private void RefreshTopTracks() => Replace(TopTracks,
        TopTracksAggregator.Aggregate(HeardTracks, SelectedTopTracksTimeframe, DateTimeOffset.Now));

    private static void Replace(ObservableCollection<RadioStation> target, IEnumerable<RadioStation> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<HeardTrack> target, IEnumerable<HeardTrack> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static void Replace(ObservableCollection<TopTrack> target, IEnumerable<TopTrack> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void UpdateLoading() => IsLoading = _loadingPopular || _loadingSearch;

    private async Task<IReadOnlyList<RadioStation>?> GetCachedAsync(string key, TimeSpan maxAge)
    {
        if (_cache is null) return null;
        try { return await _cache.GetAsync(key, maxAge); }
        catch (Exception exception)
        {
            AppDiagnostics.Record("directory.cache-read", exception);
            return null;
        }
    }

    private async Task StoreCachedAsync(string key, IReadOnlyList<RadioStation> stations)
    {
        if (_cache is null) return;
        try { await _cache.StoreAsync(key, stations); }
        catch (Exception exception) { AppDiagnostics.Record("directory.cache-write", exception); }
    }

    private void ReportPlaybackFailure(Exception exception)
    {
        AppDiagnostics.Record("station.control", exception);
        Status = "Unable to play this station. Try another station.";
    }

    private async Task ReportPlayBestEffortAsync(string stationId)
    {
        try { await _playReporter!.ReportPlayAsync(stationId).ConfigureAwait(false); }
        catch (Exception exception) { AppDiagnostics.Record("directory.play-report", exception); }
    }
}
