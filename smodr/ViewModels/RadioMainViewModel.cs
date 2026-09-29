using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using smodr.Models;
using smodr.Services;
using Windows.Media.Playback;

namespace smodr.ViewModels;

public partial class RadioMainViewModel : ObservableObject, IDisposable
{
    private readonly AudioService _audio = new();
    private readonly RadioDirectoryService _directory = new();
    private readonly RadioLibraryService _library = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _searchCancellation;

    public RadioMainViewModel()
    {
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

        IsLoading = true;
        Status = "Tuning in…";
        try
        {
            Replace(PopularStations, await _directory.GetPopularStationsAsync(60));
            Status = PopularStations.Count == 0 ? "Nothing here yet" : string.Empty;
        }
        catch (Exception ex)
        {
            Status = $"Directory unavailable: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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
        if (_searchCancellation is { } previousSearch)
        {
            await previousSearch.CancelAsync();
        }
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        IsLoading = true;
        Status = loadingStatus;
        try
        {
            var stations = await search(cancellation.Token);
            if (cancellation.IsCancellationRequested)
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
            if (!cancellation.IsCancellationRequested)
            {
                Status = $"Search unavailable: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                IsLoading = false;
            }

            cancellation.Dispose();
        }
    }

    public async Task TogglePlaybackAsync(RadioStation station)
    {
        if (CurrentStation is not null && StationMatches(CurrentStation, station))
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
            catch (IOException)
            {
                Status = "Playing, but recent stations could not be saved.";
            }
            catch (UnauthorizedAccessException)
            {
                Status = "Playing, but recent stations could not be saved.";
            }
        }
        catch (Exception ex)
        {
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

    public void Stop() => _audio.Stop();

    public void ToggleFavorite(RadioStation station)
    {
        try
        {
            _library.ToggleFavorite(station);
            RefreshLibraryCollections();
            OnPropertyChanged(nameof(CurrentStation));
        }
        catch (IOException)
        {
            Status = "Favorites could not be saved.";
        }
        catch (UnauthorizedAccessException)
        {
            Status = "Favorites could not be saved.";
        }
    }

    public bool IsFavorite(RadioStation station) => _library.IsFavorite(station);

    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _audio.Dispose();
        _directory.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Audio_StationChanged(object? sender, RadioStation station) =>
        _dispatcher.TryEnqueue(() => CurrentStation = station);

    private void Audio_PlaybackStateChanged(object? sender, MediaPlaybackState state) =>
        _dispatcher.TryEnqueue(() =>
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
        _dispatcher.TryEnqueue(() => Status = $"Unable to play this station: {message}");

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

    private static bool StationMatches(RadioStation left, RadioStation right) =>
        !string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id)
            ? string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
            : string.Equals(left.StreamUrl, right.StreamUrl, StringComparison.OrdinalIgnoreCase);
}
