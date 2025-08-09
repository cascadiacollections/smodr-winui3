using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
            PlayPauseButton.Content = ViewModel.IsPlaying ? "?" : "?";
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
            await LoadEpisodesAsync();
        }

        private async System.Threading.Tasks.Task LoadEpisodesAsync()
        {
            try
            {
                // Show loading
                LoadingPanel.Visibility = Visibility.Visible;
                EpisodesListView.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = true;
                LoadingMessage.Text = "Fetching episodes from Smodcast RSS feed...";
                RefreshButton.IsEnabled = false;

                // Load episodes
                await ViewModel.LoadEpisodesAsync();

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
    }
}
