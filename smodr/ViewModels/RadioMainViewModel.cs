using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using smodr.Models;
using smodr.Services;
using Windows.Media.Playback;

namespace smodr.ViewModels;

public partial class RadioMainViewModel : ObservableObject, IDisposable
{
    private readonly IRadioPlayer _audio;
    private readonly IRadioDirectoryService _directory;
    private readonly IRadioLibraryService _library;
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;
    private bool _loadingPopular;
    private bool _loadingSearch;

    public RadioMainViewModel(
        IRadioPlayer audio,
        IRadioDirectoryService directory,
        IRadioLibraryService library,
        Action<Action>? dispatch = null)
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
        _audio.StationChanged += Audio_StationChanged;
        _audio.PlaybackStateChanged += Audio_PlaybackStateChanged;
        _audio.PlaybackFailed += Audio_PlaybackFailed;
        RefreshLibraryCollections();
    }

    public ObservableCollection<RadioStation> PopularStations { get; } = [];
    public ObservableCollection<RadioStation> SearchResults { get; } = [];
    public ObservableCollection<RadioStation> Favorites { get; } = [];
    public ObservableCollection<RadioStation> Recents { get; } = [];

    [ObservableProperty] public partial RadioStation? CurrentStation { get; set; }
    [ObservableProperty] public partial bool IsPlaying { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Tuning in…";

    public async Task LoadPopularAsync()
    {
        if (PopularStations.Count > 0)
        {
            return;
        }

        _loadingPopular = true;
        UpdateLoading();
        Status = "Tuning in…";
        try
        {
            Replace(PopularStations, await _directory.GetPopularStationsAsync(60, _lifetimeCancellation.Token));
            Status = PopularStations.Count == 0 ? "Nothing here yet" : string.Empty;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppDiagnostics.Record("popular.load", ex);
            Status = $"Directory unavailable: {ex.Message}";
        }
        finally
        {
            _loadingPopular = false;
            UpdateLoading();
        }
    }

    public Task SearchAsync(string query) => SearchCoreAsync(
        token => _directory.SearchAsync(query, cancellationToken: token),
        string.IsNullOrWhiteSpace(query) ? "Browse by genre" : "Searching…");

    public Task SearchGenreAsync(string genre) => SearchCoreAsync(
        token => _directory.SearchGenreAsync(genre, cancellationToken: token),
        $"Browsing {genre}…");

    private async Task SearchCoreAsync(
        Func<CancellationToken, Task<IReadOnlyList<RadioStation>>> search,
        string loadingStatus)
    {
        var version = Interlocked.Increment(ref _searchVersion);
        if (_searchCancellation is { } previousSearch)
        {
            await previousSearch.CancelAsync();
        }
        if (version != Volatile.Read(ref _searchVersion) || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _searchCancellation = cancellation;
        _loadingSearch = true;
        UpdateLoading();
        Status = loadingStatus;
        try
        {
            var stations = await search(cancellation.Token);
            if (cancellation.IsCancellationRequested || version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            Replace(SearchResults, stations);
            Status = SearchResults.Count == 0 ? "No matching stations" : string.Empty;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && version == Volatile.Read(ref _searchVersion))
            {
                AppDiagnostics.Record("directory.search", ex);
                Status = $"Search unavailable: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                _loadingSearch = false;
                UpdateLoading();
            }

            cancellation.Dispose();
        }
    }

    public async Task TogglePlaybackAsync(RadioStation station)
    {
        if (CurrentStation is not null && RadioStationIdentity.Matches(CurrentStation, station))
        {
            if (_audio.IsPlaying)
            {
                _audio.Pause();
            }
            else
            {
                _audio.Play();
            }

            return;
        }

        try
        {
            await _audio.PlayStationAsync(station);
            try
            {
                _library.LogRecent(station);
                RefreshLibraryCollections();
            }
            catch (IOException exception)
            {
                AppDiagnostics.Record("library.recent-save", exception);
                Status = "Playing, but recent stations could not be saved.";
            }
            catch (UnauthorizedAccessException exception)
            {
                AppDiagnostics.Record("library.recent-save", exception);
                Status = "Playing, but recent stations could not be saved.";
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Record("station.play", ex);
            Status = $"Unable to play {station.Name}: {ex.Message}";
        }
    }

    public void PlayPause()
    {
        if (_audio.IsPlaying)
        {
            _audio.Pause();
        }
        else
        {
            _audio.Play();
        }
    }

    public void Stop()
    {
        _audio.StopStation();
        IsPlaying = false;
    }

    public void ToggleFavorite(RadioStation station)
    {
        try
        {
            _library.ToggleFavorite(station);
            RefreshLibraryCollections();
            OnPropertyChanged(nameof(CurrentStation));
        }
        catch (IOException exception)
        {
            AppDiagnostics.Record("library.favorite-save", exception);
            Status = "Favorites could not be saved.";
        }
        catch (UnauthorizedAccessException exception)
        {
            AppDiagnostics.Record("library.favorite-save", exception);
            Status = "Favorites could not be saved.";
        }
    }

    public bool IsFavorite(RadioStation station) => _library.IsFavorite(station);

    public void Dispose()
    {
        _audio.StationChanged -= Audio_StationChanged;
        _audio.PlaybackStateChanged -= Audio_PlaybackStateChanged;
        _audio.PlaybackFailed -= Audio_PlaybackFailed;
        _lifetimeCancellation.Cancel();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Audio_StationChanged(object? sender, RadioStation? station) =>
        _dispatch(() => CurrentStation = station);

    private void Audio_PlaybackStateChanged(object? sender, MediaPlaybackState state) =>
        _dispatch(() =>
        {
            IsPlaying = state == MediaPlaybackState.Playing;
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
        _dispatch(() => Status = $"Unable to play this station: {message}");

    private void RefreshLibraryCollections()
    {
        Replace(Favorites, _library.Favorites);
        Replace(Recents, _library.Recents);
    }

    private static void Replace(ObservableCollection<RadioStation> target, IEnumerable<RadioStation> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void UpdateLoading() => IsLoading = _loadingPopular || _loadingSearch;
}
