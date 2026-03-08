using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Media.Playback;
using smodr.Models;
using smodr.Services;

namespace smodr.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DataService _dataService = new();
    private readonly DownloadService _downloadService = new();
    private readonly AudioService _audioService = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingMessage = "Loading episodes...";

    [ObservableProperty]
    private Episode? _selectedEpisode;

    [ObservableProperty]
    private Episode? _currentPlayingEpisode;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    private TimeSpan _currentPosition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    private TimeSpan _duration;

    [ObservableProperty]
    private string _playbackStatus = "Stopped";

    public string FormattedPosition => $"{CurrentPosition:mm\\:ss}";
    public string FormattedDuration => $"{Duration:mm\\:ss}";

    public ObservableCollection<Episode> Episodes { get; } = [];

    public MainViewModel()
    {
        _audioService.EpisodeChanged += AudioService_EpisodeChanged;
        _audioService.PlaybackStateChanged += AudioService_PlaybackStateChanged;
        _audioService.PositionChanged += AudioService_PositionChanged;
        _audioService.DurationChanged += AudioService_DurationChanged;
    }

    [RelayCommand]
    public async Task LoadEpisodesAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        LoadingMessage = forceRefresh ? "Refreshing episodes from Smodcast RSS feed..." : "Loading episodes...";

        try
        {
            var episodes = await _dataService.GetEpisodesAsync(forceRefresh);

            Episodes.Clear();
            foreach (var episode in episodes)
            {
                Episodes.Add(episode);
            }

            if (Episodes.Count == 0)
            {
                LoadingMessage = "No episodes found. Please check your internet connection.";
            }
            else
            {
                var cacheInfo = await _dataService.GetCacheInfoAsync();
                if (cacheInfo is not null)
                {
                    Debug.WriteLine($"Cache info: {Episodes.Count} episodes, last updated: {cacheInfo.LastUpdated:yyyy-MM-dd HH:mm:ss}");
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
            return;

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
            _audioService.Pause();
        else
            _audioService.Play();
    }

    [RelayCommand]
    public void Stop() => _audioService.Stop();

    public async Task DownloadEpisodeAsync(Episode? episode, nint windowHandle)
    {
        if (episode is null || string.IsNullOrEmpty(episode.MediaUrl))
            return;

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
        try
        {
            return await _dataService.GetCacheInfoAsync();
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
        try
        {
            return await _dataService.GetCachedEpisodesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting cached episodes: {ex.Message}");
            return null;
        }
    }

    private void AudioService_EpisodeChanged(object? sender, Episode episode) =>
        CurrentPlayingEpisode = episode;

    private void AudioService_PlaybackStateChanged(object? sender, MediaPlaybackState state)
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
    }

    private void AudioService_PositionChanged(object? sender, TimeSpan position) =>
        CurrentPosition = position;

    private void AudioService_DurationChanged(object? sender, TimeSpan duration) =>
        Duration = duration;

    public void Dispose()
    {
        _audioService?.Dispose();
        _dataService?.Dispose();
        _downloadService?.Dispose();
    }
}
