using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using smodr.Models;
using smodr.Services;
using Windows.Media.Playback;

namespace smodr.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioService _audioService = new();
    private readonly DataService _dataService = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly DownloadService _downloadService = new();

    public MainViewModel()
    {
        _audioService.EpisodeChanged += AudioService_EpisodeChanged;
        _audioService.PlaybackStateChanged += AudioService_PlaybackStateChanged;
        _audioService.PositionChanged += AudioService_PositionChanged;
        _audioService.DurationChanged += AudioService_DurationChanged;
    }

    [ObservableProperty] public partial bool IsLoading { get; set; }

    [ObservableProperty] public partial string LoadingMessage { get; set; } = "Loading episodes...";

    [ObservableProperty] public partial Episode? SelectedEpisode { get; set; }

    [ObservableProperty] public partial Episode? CurrentPlayingEpisode { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial bool IsPaused { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    public partial TimeSpan CurrentPosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    public partial TimeSpan Duration { get; set; }

    [ObservableProperty] public partial string PlaybackStatus { get; set; } = "Stopped";

    [ObservableProperty] public partial Podcast? SelectedPodcast { get; set; }

    public string FormattedPosition => $"{CurrentPosition:mm\\:ss}";
    public string FormattedDuration => $"{Duration:mm\\:ss}";

    public ObservableCollection<Episode> Episodes { get; } = [];

    public void Dispose()
    {
        _audioService.Dispose();
        _dataService.Dispose();
        _downloadService.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public async Task LoadEpisodesAsync(bool forceRefresh = false)
    {
        if (SelectedPodcast is not { } podcast)
        {
            return;
        }

        IsLoading = true;
        LoadingMessage = forceRefresh
            ? $"Refreshing {podcast.Name} episodes..."
            : $"Loading {podcast.Name} episodes...";

        try
        {
            var episodes = await _dataService.GetEpisodesAsync(podcast.Id, podcast.FeedUrl, forceRefresh);

            Episodes.Clear();
            foreach (var episode in episodes)
            {
                Episodes.Add(episode);
            }

            if (Episodes.Count == 0)
            {
                LoadingMessage = "No episodes found. The feed may be unavailable.";
            }
            else
            {
                var cacheInfo = await _dataService.GetCacheInfoAsync(podcast.Id);
                if (cacheInfo is not null)
                {
                    Debug.WriteLine(
                        $"Cache info for {podcast.Id}: {Episodes.Count} episodes, last updated: {cacheInfo.LastUpdated:yyyy-MM-dd HH:mm:ss}");
                }
            }
        }
        catch (Exception ex)
        {
            LoadingMessage = $"Error loading episodes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectEpisode(Episode? episode)
    {
        if (episode is not null)
        {
            SelectedEpisode = episode;
        }
    }

    [RelayCommand]
    public async Task PlayEpisodeAsync(Episode? episode)
    {
        if (episode is null || string.IsNullOrEmpty(episode.MediaUrl))
        {
            return;
        }

        try
        {
            await _audioService.PlayEpisodeAsync(episode);
            SelectedEpisode = episode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback failed: {ex.Message}");
            PlaybackStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void PlayPause()
    {
        if (_audioService.IsPlaying)
        {
            _audioService.Pause();
        }
        else
        {
            _audioService.Play();
        }
    }

    [RelayCommand]
    public void Stop() => _audioService.Stop();

    public void Seek(TimeSpan position) => _audioService.SetPosition(position);

    public async Task DownloadEpisodeAsync(Episode? episode, nint windowHandle)
    {
        if (episode is null || string.IsNullOrEmpty(episode.MediaUrl))
        {
            return;
        }

        try
        {
            var success = await _downloadService.DownloadEpisodeAsync(episode, windowHandle);
            if (success)
            {
                Debug.WriteLine($"Successfully downloaded: {episode.Title}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> ClearCacheAsync()
    {
        try
        {
            return await _dataService.ClearCacheAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error clearing cache: {ex.Message}");
            return false;
        }
    }

    public async Task<CacheMetadata?> GetCacheInfoAsync()
    {
        if (SelectedPodcast is not { } podcast)
        {
            return null;
        }

        try
        {
            return await _dataService.GetCacheInfoAsync(podcast.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting cache info: {ex.Message}");
            return null;
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        try
        {
            return await _dataService.GetCacheSizeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting cache size: {ex.Message}");
            return 0;
        }
    }

    public async Task<List<Episode>?> GetCachedEpisodesAsync()
    {
        if (SelectedPodcast is not { } podcast)
        {
            return null;
        }

        try
        {
            return await _dataService.GetCachedEpisodesAsync(podcast.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting cached episodes: {ex.Message}");
            return null;
        }
    }

    private void AudioService_EpisodeChanged(object? sender, Episode episode) =>
        _dispatcherQueue.TryEnqueue(() => CurrentPlayingEpisode = episode);

    private void AudioService_PlaybackStateChanged(object? sender, MediaPlaybackState state)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsPlaying = state == MediaPlaybackState.Playing;
            IsPaused = state == MediaPlaybackState.Paused;

            PlaybackStatus = state switch
            {
                MediaPlaybackState.Playing => "Playing",
                MediaPlaybackState.Paused => "Paused",
                MediaPlaybackState.None => "Stopped",
                MediaPlaybackState.Buffering => "Buffering",
                MediaPlaybackState.Opening => "Loading",
                _ => "Unknown"
            };

            OnPropertyChanged(nameof(FormattedPosition));
            OnPropertyChanged(nameof(FormattedDuration));
        });
    }

    private void AudioService_PositionChanged(object? sender, TimeSpan position) =>
        _dispatcherQueue.TryEnqueue(() => CurrentPosition = position);

    private void AudioService_DurationChanged(object? sender, TimeSpan duration) =>
        _dispatcherQueue.TryEnqueue(() => Duration = duration);
}
