using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using smodr.ViewModels;
using smodr.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace smodr
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = new MainViewModel();
            
            // Subscribe to ViewModel events for UI updates
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Initially hide media controls
            MediaControlsPanel.Visibility = Visibility.Collapsed;
            
            // Load episodes when the window is activated
            this.Activated += MainWindow_Activated;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Ensure UI updates happen on the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                // Update UI elements based on ViewModel property changes
                switch (e.PropertyName)
                {
                    case nameof(ViewModel.CurrentPlayingEpisode):
                        UpdateNowPlayingInfo();
                        break;
                    case nameof(ViewModel.IsPlaying):
                        UpdatePlayPauseButton();
                        break;
                    case nameof(ViewModel.PlaybackStatus):
                        UpdatePlaybackStatus();
                        break;
                    case nameof(ViewModel.FormattedPosition):
                        UpdateTimeDisplay();
                        break;
                    case nameof(ViewModel.FormattedDuration):
                        UpdateTimeDisplay();
                        break;
                }
            });
        }

        private void UpdateNowPlayingInfo()
        {
            if (ViewModel.CurrentPlayingEpisode != null)
            {
                MediaControlsPanel.Visibility = Visibility.Visible;
                CurrentEpisodeTitle.Text = ViewModel.CurrentPlayingEpisode.Title;
                CurrentEpisodeImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new System.Uri(ViewModel.CurrentPlayingEpisode.ImageUrl));
            }
            else
            {
                MediaControlsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdatePlayPauseButton()
        {
            PlayPauseButton.Content = ViewModel.IsPlaying ? "⏸" : "▶";
        }

        private void UpdatePlaybackStatus()
        {
            PlaybackStatusText.Text = ViewModel.PlaybackStatus;
        }

        private void UpdateTimeDisplay()
        {
            PositionText.Text = ViewModel.FormattedPosition;
            DurationText.Text = ViewModel.FormattedDuration;
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                // Only load episodes once
                this.Activated -= MainWindow_Activated;
                await LoadEpisodesAsync();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadEpisodesAsync(forceRefresh: true);
        }

        private async System.Threading.Tasks.Task LoadEpisodesAsync(bool forceRefresh = false)
        {
            try
            {
                // If not forcing refresh, try to load from cache first without showing loading
                if (!forceRefresh)
                {
                    // Quick check if we have cached data
                    var cachedEpisodes = await ViewModel.GetCachedEpisodesAsync();
                    if (cachedEpisodes is not null && cachedEpisodes.Count > 0)
                    {
                        // Show cached data immediately
                        EpisodesListView.ItemsSource = cachedEpisodes;
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        EpisodesListView.Visibility = Visibility.Visible;
                        LoadingRing.IsActive = false;
                        RefreshButton.IsEnabled = true;
                        
                        // Update ViewModel episodes collection
                        ViewModel.Episodes.Clear();
                        foreach (var episode in cachedEpisodes)
                        {
                            ViewModel.Episodes.Add(episode);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Loaded {cachedEpisodes.Count} episodes from cache instantly");
                        return;
                    }
                }

                // Show loading only when we need to fetch from network
                LoadingPanel.Visibility = Visibility.Visible;
                EpisodesListView.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = true;
                LoadingMessage.Text = forceRefresh ? "Refreshing episodes from Smodcast RSS feed..." : "Loading episodes from network...";
                RefreshButton.IsEnabled = false;

                // Load episodes (this will hit network or show cache if not available)
                await ViewModel.LoadEpisodesAsync(forceRefresh);

                // Update UI
                EpisodesListView.ItemsSource = ViewModel.Episodes;

                // Hide loading, show episodes
                LoadingPanel.Visibility = Visibility.Collapsed;
                EpisodesListView.Visibility = Visibility.Visible;
                LoadingRing.IsActive = false;

                if (ViewModel.Episodes.Count == 0)
                {
                    LoadingMessage.Text = "No episodes found. Please check your internet connection.";
                    LoadingPanel.Visibility = Visibility.Visible;
                    EpisodesListView.Visibility = Visibility.Collapsed;
                }
            }
            catch (System.Exception ex)
            {
                // Show error
                LoadingMessage.Text = $"Error loading episodes: {ex.Message}";
                LoadingPanel.Visibility = Visibility.Visible;
                EpisodesListView.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = false;
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private void EpisodesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                var selectedEpisode = e.AddedItems[0] as Episode;
                ViewModel.SelectEpisode(selectedEpisode);
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Episode episode)
            {
                try
                {
                    await ViewModel.PlayEpisodeAsync(episode);
                }
                catch (System.Exception ex)
                {
                    await ShowErrorDialogAsync("Playback Error", $"Failed to play episode: {ex.Message}");
                }
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.PlayPause();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Stop();
        }

        private async void DownloadMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is Episode episode)
            {
                try
                {
                    // Show a loading indicator or disable the menu item
                    menuItem.IsEnabled = false;
                    menuItem.Text = "Downloading...";

                    await ViewModel.DownloadEpisodeAsync(episode);

                    // Show success message
                    var dialog = new ContentDialog
                    {
                        Title = "Download Complete",
                        Content = $"Successfully downloaded: {episode.Title}",
                        CloseButtonText = "OK"
                    };
                    
                    // Set XamlRoot for the dialog
                    dialog.XamlRoot = this.Content.XamlRoot;
                    await dialog.ShowAsync();
                }
                catch (System.Exception ex)
                {
                    await ShowErrorDialogAsync("Download Failed", $"Failed to download episode: {ex.Message}");
                }
                finally
                {
                    // Reset menu item
                    menuItem.IsEnabled = true;
                    menuItem.Text = "Download Episode";
                }
            }
        }

        private async void PlayMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is Episode episode)
            {
                try
                {
                    await ViewModel.PlayEpisodeAsync(episode);
                }
                catch (System.Exception ex)
                {
                    await ShowErrorDialogAsync("Playback Error", $"Failed to play episode: {ex.Message}");
                }
            }
        }

        private async System.Threading.Tasks.Task ShowErrorDialogAsync(string title, string message)
        {
            var errorDialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };
            
            errorDialog.XamlRoot = this.Content.XamlRoot;
            await errorDialog.ShowAsync();
        }

        private async void CacheInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cacheInfo = await ViewModel.GetCacheInfoAsync();
                var cacheSize = await ViewModel.GetCacheSizeAsync();
                
                string message;
                if (cacheInfo != null)
                {
                    var cacheSizeFormatted = cacheSize > 0 ? $"{cacheSize / 1024.0:F1} KB" : "Unknown";
                    message = $"Episodes: {cacheInfo.EpisodeCount}\n" +
                             $"Last Updated: {cacheInfo.LastUpdated:yyyy-MM-dd HH:mm:ss}\n" +
                             $"Cache Size: {cacheSizeFormatted}\n" +
                             $"Version: {cacheInfo.Version}";
                }
                else
                {
                    message = "No cache data available.";
                }

                var dialog = new ContentDialog
                {
                    Title = "Cache Information",
                    Content = message,
                    CloseButtonText = "OK"
                };
                
                dialog.XamlRoot = this.Content.XamlRoot;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("Error", $"Failed to get cache information: {ex.Message}");
            }
        }

        private async void ClearCacheMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "Clear Cache",
                    Content = "Are you sure you want to clear the cache? This will force a fresh download of episodes on next refresh.",
                    PrimaryButtonText = "Clear",
                    CloseButtonText = "Cancel"
                };
                
                confirmDialog.XamlRoot = this.Content.XamlRoot;
                var result = await confirmDialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary)
                {
                    var success = await ViewModel.ClearCacheAsync();
                    
                    var resultDialog = new ContentDialog
                    {
                        Title = success ? "Success" : "Error",
                        Content = success ? "Cache cleared successfully." : "Failed to clear cache.",
                        CloseButtonText = "OK"
                    };
                    
                    resultDialog.XamlRoot = this.Content.XamlRoot;
                    await resultDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("Error", $"Failed to clear cache: {ex.Message}");
            }
        }

        private async void ForceRefreshMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await LoadEpisodesAsync(forceRefresh: true);
        }
    }
}
