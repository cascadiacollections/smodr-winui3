using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Models;
using smodr.ViewModels;

namespace smodr;

/// <summary>
/// Main application window for the Smodcast player.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _updatingSliderProgrammatically;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();

        // Custom title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        MediaControlsPanel.Visibility = Visibility.Collapsed;

        Activated += MainWindow_Activated;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.CurrentPlayingEpisode):
                UpdateNowPlayingInfo();
                break;
            case nameof(ViewModel.IsPlaying):
                UpdatePlayPauseButton();
                break;
            case nameof(ViewModel.PlaybackStatus):
                PlaybackStatusText.Text = ViewModel.PlaybackStatus;
                break;
            case nameof(ViewModel.CurrentPosition):
                UpdateSeekPosition();
                PositionText.Text = ViewModel.FormattedPosition;
                break;
            case nameof(ViewModel.Duration):
                SeekSlider.Maximum = Math.Max(1, ViewModel.Duration.TotalSeconds);
                DurationText.Text = ViewModel.FormattedDuration;
                break;
        }
    }

    private void UpdateNowPlayingInfo()
    {
        if (ViewModel.CurrentPlayingEpisode is { } episode)
        {
            MediaControlsPanel.Visibility = Visibility.Visible;
            CurrentEpisodeTitle.Text = episode.Title;
            CurrentEpisodeImage.Source = new BitmapImage(new Uri(episode.ImageUrl));
        }
        else
        {
            MediaControlsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePlayPauseButton() =>
        PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768";

    private void UpdateSeekPosition()
    {
        _updatingSliderProgrammatically = true;
        SeekSlider.Value = ViewModel.CurrentPosition.TotalSeconds;
        _updatingSliderProgrammatically = false;
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingSliderProgrammatically)
        {
            ViewModel.Seek(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            return;

        Activated -= MainWindow_Activated;
        await LoadEpisodesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadEpisodesAsync(forceRefresh: true);

    private async Task LoadEpisodesAsync(bool forceRefresh = false)
    {
        try
        {
            if (!forceRefresh)
            {
                var cachedEpisodes = await ViewModel.GetCachedEpisodesAsync();
                if (cachedEpisodes is { Count: > 0 })
                {
                    EpisodesListView.ItemsSource = cachedEpisodes;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    EpisodesListView.Visibility = Visibility.Visible;
                    LoadingRing.IsActive = false;
                    RefreshButton.IsEnabled = true;

                    ViewModel.Episodes.Clear();
                    foreach (var episode in cachedEpisodes)
                    {
                        ViewModel.Episodes.Add(episode);
                    }

                    Debug.WriteLine($"Loaded {cachedEpisodes.Count} episodes from cache instantly");
                    return;
                }
            }

            LoadingPanel.Visibility = Visibility.Visible;
            EpisodesListView.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            LoadingMessage.Text = forceRefresh
                ? "Refreshing episodes from Smodcast RSS feed..."
                : "Loading episodes from network...";
            RefreshButton.IsEnabled = false;

            await ViewModel.LoadEpisodesAsync(forceRefresh);

            EpisodesListView.ItemsSource = ViewModel.Episodes;

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
        catch (Exception ex)
        {
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
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is Episode episode)
        {
            ViewModel.SelectEpisode(episode);
        }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Episode episode })
        {
            try
            {
                await ViewModel.PlayEpisodeAsync(episode);
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("Playback Error", $"Failed to play episode: {ex.Message}");
            }
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.PlayPause();

    private void StopButton_Click(object sender, RoutedEventArgs e) =>
        ViewModel.Stop();

    private async void DownloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: Episode episode } menuItem)
            return;

        try
        {
            menuItem.IsEnabled = false;
            menuItem.Text = "Downloading...";

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            await ViewModel.DownloadEpisodeAsync(episode, hWnd);

            await ShowDialogAsync("Download Complete", $"Successfully downloaded: {episode.Title}");
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Download Failed", $"Failed to download episode: {ex.Message}");
        }
        finally
        {
            menuItem.IsEnabled = true;
            menuItem.Text = "Download Episode";
        }
    }

    private async void PlayMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: Episode episode })
        {
            try
            {
                await ViewModel.PlayEpisodeAsync(episode);
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("Playback Error", $"Failed to play episode: {ex.Message}");
            }
        }
    }

    private async void CacheInfoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cacheInfo = await ViewModel.GetCacheInfoAsync();
            var cacheSize = await ViewModel.GetCacheSizeAsync();

            var message = cacheInfo is not null
                ? $"Episodes: {cacheInfo.EpisodeCount}\n" +
                  $"Last Updated: {cacheInfo.LastUpdated:yyyy-MM-dd HH:mm:ss}\n" +
                  $"Cache Size: {(cacheSize > 0 ? $"{cacheSize / 1024.0:F1} KB" : "Unknown")}\n" +
                  $"Version: {cacheInfo.Version}"
                : "No cache data available.";

            await ShowDialogAsync("Cache Information", message);
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
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var success = await ViewModel.ClearCacheAsync();
                await ShowDialogAsync(
                    success ? "Success" : "Error",
                    success ? "Cache cleared successfully." : "Failed to clear cache.");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Error", $"Failed to clear cache: {ex.Message}");
        }
    }

    private async void ForceRefreshMenuItem_Click(object sender, RoutedEventArgs e) =>
        await LoadEpisodesAsync(forceRefresh: true);

    private Task ShowErrorDialogAsync(string title, string message) =>
        ShowDialogAsync(title, message);

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
