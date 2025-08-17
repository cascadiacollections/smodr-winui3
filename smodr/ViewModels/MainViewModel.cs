using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Media.Playback;
using smodr.Models;
using smodr.Services;

namespace smodr.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DataService _dataService;
        private readonly DownloadService _downloadService;
        private readonly AudioService _audioService;
        private bool _isLoading;
        private string _loadingMessage = "Loading episodes...";
        private Episode? _selectedEpisode;
        private Episode? _currentPlayingEpisode;
        private bool _isPlaying;
        private bool _isPaused;
        private TimeSpan _currentPosition;
        private TimeSpan _duration;
        private string _playbackStatus = "Stopped";

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public Episode? SelectedEpisode
        {
            get => _selectedEpisode;
            set => SetProperty(ref _selectedEpisode, value);
        }

        public Episode? CurrentPlayingEpisode
        {
            get => _currentPlayingEpisode;
            set => SetProperty(ref _currentPlayingEpisode, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            set => SetProperty(ref _currentPosition, value);
        }

        public TimeSpan Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        public string PlaybackStatus
        {
            get => _playbackStatus;
            set => SetProperty(ref _playbackStatus, value);
        }

        public string FormattedPosition => $"{CurrentPosition:mm\\:ss}";
        public string FormattedDuration => $"{Duration:mm\\:ss}";

        public ObservableCollection<Episode> Episodes { get; } = new();

        public ICommand LoadEpisodesCommand { get; }
        public ICommand RefreshEpisodesCommand { get; }
        public ICommand SelectEpisodeCommand { get; }
        public ICommand DownloadEpisodeCommand { get; }
        public ICommand PlayEpisodeCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }

        public MainViewModel()
        {
            _dataService = new DataService();
            _downloadService = new DownloadService();
            _audioService = new AudioService();

            LoadEpisodesCommand = new AsyncRelayCommand(() => LoadEpisodesAsync());
            RefreshEpisodesCommand = new AsyncRelayCommand(() => LoadEpisodesAsync(true));
            SelectEpisodeCommand = new RelayCommand<Episode>(SelectEpisode);
            DownloadEpisodeCommand = new AsyncRelayCommand<Episode>(episode => DownloadEpisodeAsync(episode));
            PlayEpisodeCommand = new AsyncRelayCommand<Episode>(PlayEpisodeAsync);
            PlayPauseCommand = new RelayCommand(PlayPause);
            StopCommand = new RelayCommand(Stop);

            // Subscribe to audio service events
            _audioService.EpisodeChanged += AudioService_EpisodeChanged;
            _audioService.PlaybackStateChanged += AudioService_PlaybackStateChanged;
            _audioService.PositionChanged += AudioService_PositionChanged;
            _audioService.DurationChanged += AudioService_DurationChanged;
        }

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
                    // Show cache info in debug
                    var cacheInfo = await _dataService.GetCacheInfoAsync();
                    if (cacheInfo != null)
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
            if (episode != null)
            {
                SelectedEpisode = episode;
            }
        }

        public async Task PlayEpisodeAsync(Episode? episode)
        {
            if (episode == null || string.IsNullOrEmpty(episode.MediaUrl))
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

        public void Stop()
        {
            _audioService.Stop();
        }

        public async Task DownloadEpisodeAsync(Episode? episode, object? window = null)
        {
            if (episode == null || string.IsNullOrEmpty(episode.MediaUrl))
                return;

            try
            {
                var success = await _downloadService.DownloadEpisodeAsync(episode, window);
                if (success)
                {
                    Debug.WriteLine($"Successfully downloaded: {episode.Title}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Download failed: {ex.Message}");
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

        private void AudioService_EpisodeChanged(object? sender, Episode episode)
        {
            CurrentPlayingEpisode = episode;
        }

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

            // Notify UI that formatted properties have changed
            OnPropertyChanged(nameof(FormattedPosition));
            OnPropertyChanged(nameof(FormattedDuration));
        }

        private void AudioService_PositionChanged(object? sender, TimeSpan position)
        {
            CurrentPosition = position;
            OnPropertyChanged(nameof(FormattedPosition));
        }

        private void AudioService_DurationChanged(object? sender, TimeSpan duration)
        {
            Duration = duration;
            OnPropertyChanged(nameof(FormattedDuration));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        // Add this method to MainViewModel to fix CS1061
        public bool IsCacheValid()
        {
            // Implement your cache validation logic here.
            // For example, check if Episodes is not empty and cache is not expired.
            // This is a placeholder implementation:
            return Episodes != null && Episodes.Count > 0;
        }
    }

    // Simple relay command implementations
    public class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
    {
        private readonly Action<T?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute == null || canExecute((T?)parameter);
        }

        public void Execute(object? parameter)
        {
            _execute((T?)parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute == null || canExecute();
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
    {
        private readonly Func<Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (canExecute == null || canExecute());
        }

        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class AsyncRelayCommand<T>(Func<T?, Task> execute, Func<T?, bool>? canExecute = null) : ICommand
    {
        private readonly Func<T?, Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (canExecute == null || canExecute((T?)parameter));
        }

        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _execute((T?)parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}