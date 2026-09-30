using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using smodr.Models;
using smodr.Services;
using smodr.ViewModels;

namespace smodr;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private bool _closingAfterFlush;
    private bool _closeAfterFlush;
    private bool _closed;
    private bool _settingsReady;
    private readonly DispatcherTimer _sleepCountdownTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public RadioMainViewModel ViewModel { get; }

    public MainWindow(RadioMainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PlayReportingSwitch.IsOn = ViewModel.IsPlayReportingEnabled;
        _settingsReady = true;
        _sleepCountdownTimer.Tick += SleepCountdownTimer_Tick;
        UpdateSleepTimer();
        ConfigureWindow();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Recents.CollectionChanged += LibraryCollectionChanged;
        ViewModel.Favorites.CollectionChanged += LibraryCollectionChanged;
        ViewModel.HeardTracks.CollectionChanged += LibraryCollectionChanged;

        AppNavigation.SelectedItem = ListenNowItem;
        UpdateLibraryVisibility();
        Closed += MainWindow_Closed;
        _ = LoadAndWarmAsync();
    }

    private async Task LoadAndWarmAsync()
    {
        await ViewModel.LoadPopularAsync();
        if (_closed || ViewModel.PopularStations.Count == 0 || ViewModel.Status.Length != 0) return;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (!_closed && WindowsWarmupPolicy.CanPrefetch()) await ViewModel.WarmGenresAsync();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow = appWindow;
        appWindow.Closing += AppWindow_Closing;
        appWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _sleepCountdownTimer.Stop();
        _sleepCountdownTimer.Tick -= SleepCountdownTimer_Tick;
        if (_appWindow is not null)
        {
            _appWindow.Closing -= AppWindow_Closing;
        }
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Recents.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.Favorites.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.HeardTracks.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.Dispose();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeAfterFlush)
        {
            return;
        }

        var pending = Task.WhenAll(ViewModel.FlushLibraryAsync(), ViewModel.FlushDirectoryCacheAsync(), ViewModel.FlushPrivacySettingsAsync(), ViewModel.FlushTrackHistoryAsync());
        if (pending.IsCompleted)
        {
            return;
        }

        args.Cancel = true;
        if (_closingAfterFlush)
        {
            return;
        }

        _closingAfterFlush = true;
        try
        {
            do
            {
                await pending;
                pending = Task.WhenAll(ViewModel.FlushLibraryAsync(), ViewModel.FlushDirectoryCacheAsync(), ViewModel.FlushPrivacySettingsAsync(), ViewModel.FlushTrackHistoryAsync());
            } while (!pending.IsCompleted);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Record("library.flush", exception);
        }
        finally
        {
            _closeAfterFlush = true;
            Close();
        }
    }

    private void AppNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ListenNowView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Collapsed;
        FavoritesView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;

        if (args.IsSettingsSelected)
        {
            SettingsView.Visibility = Visibility.Visible;
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "search":
                SearchView.Visibility = Visibility.Visible;
                StationSearchBox.Focus(FocusState.Programmatic);
                break;
            case "favorites":
                FavoritesView.Visibility = Visibility.Visible;
                UpdateLibraryVisibility();
                break;
            default:
                ListenNowView.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PopularStations.Clear();
        await ViewModel.LoadPopularAsync();
    }

    private async void StationGrid_ItemClick(object sender, ItemClickEventArgs e) =>
        await PlayStationAsync(e.ClickedItem as RadioStation);

    private async void StationList_ItemClick(object sender, ItemClickEventArgs e) =>
        await PlayStationAsync(e.ClickedItem as RadioStation);

    private async void StationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RadioStation station })
        {
            await PlayStationAsync(station);
        }
    }

    private async Task PlayStationAsync(RadioStation? station)
    {
        if (station is null)
        {
            return;
        }

        await ViewModel.TogglePlaybackAsync(station);
        if (!_closed)
        {
            UpdateLibraryVisibility();
        }
    }

    private async void StationSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        GenreSection.Visibility = Visibility.Collapsed;
        SearchResultsList.Visibility = Visibility.Visible;
        await ViewModel.SearchAsync(args.QueryText);
    }

    private async void GenreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string genre })
        {
            StationSearchBox.Text = genre;
            GenreSection.Visibility = Visibility.Collapsed;
            SearchResultsList.Visibility = Visibility.Visible;
            await ViewModel.SearchGenreAsync(genre);
        }
    }

    private async void FavoriteNowPlayingButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentStation is { } station)
        {
            await ViewModel.ToggleFavoriteAsync(station);
        }
    }

    private async void StationFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RadioStation station })
        {
            await ViewModel.ToggleFavoriteAsync(station);
        }
    }

    private async void StationRow_FavoriteRequested(object? sender, RadioStation station) =>
        await ViewModel.ToggleFavoriteAsync(station);

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => ViewModel.PlayPause();
    private void StopButton_Click(object sender, RoutedEventArgs e) => ViewModel.Stop();

    private void SleepTimerDuration_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string minutesText }
            && int.TryParse(minutesText, out var minutes))
        {
            ViewModel.StartSleepTimer(TimeSpan.FromMinutes(minutes));
        }
    }

    private void CancelSleepTimer_Click(object sender, RoutedEventArgs e) => ViewModel.CancelSleepTimer();

    private void SleepCountdownTimer_Tick(object? sender, object e) => UpdateSleepTimer();

    private void UpdateSleepTimer()
    {
        var remaining = ViewModel.SleepTimerRemaining;
        if (remaining is null)
        {
            _sleepCountdownTimer.Stop();
            SleepTimerButton.Content = "Sleep";
            CancelSleepTimerItem.IsEnabled = false;
            AutomationProperties.SetName(SleepTimerButton, "Set sleep timer");
            ToolTipService.SetToolTip(SleepTimerButton, "Set a sleep timer");
            return;
        }

        _sleepCountdownTimer.Start();
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.Value.TotalMinutes));
        SleepTimerButton.Content = $"Sleep · {minutes}m";
        CancelSleepTimerItem.IsEnabled = true;
        AutomationProperties.SetName(SleepTimerButton, $"Sleep timer, {minutes} minutes remaining");
        ToolTipService.SetToolTip(SleepTimerButton, $"Playback pauses in {minutes} minutes");
    }

    private async void PlayReportingSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        try { await ViewModel.SetPlayReportingEnabledAsync(PlayReportingSwitch.IsOn); }
        catch (Exception exception)
        {
            AppDiagnostics.Record("privacy.write", exception);
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Setting not saved";
            StatusInfoBar.Message = "Your play-reporting choice could not be saved on this device.";
            StatusInfoBar.IsOpen = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.CurrentStation):
            case nameof(ViewModel.CurrentTrack):
                UpdateNowPlaying();
                break;
            case nameof(ViewModel.IsPlaying):
                PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768";
                AutomationProperties.SetName(PlayPauseButton, ViewModel.IsPlaying ? "Pause" : "Play");
                break;
            case nameof(ViewModel.IsLoading):
                LoadingRing.IsActive = ViewModel.IsLoading;
                LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(ViewModel.Status):
                UpdateStatus();
                break;
            case nameof(ViewModel.SleepTimerEndsAt):
                UpdateSleepTimer();
                break;
        }
    }

    private void UpdateNowPlaying()
    {
        if (ViewModel.CurrentStation is not { } station)
        {
            MiniPlayer.Visibility = Visibility.Collapsed;
            return;
        }

        MiniPlayer.Visibility = Visibility.Visible;
        NowPlayingTitle.Text = ViewModel.CurrentTrack?.Display ?? station.Name;
        NowPlayingSubtitle.Text = string.IsNullOrWhiteSpace(ViewModel.Status)
            ? ViewModel.CurrentTrack is null ? station.Details : station.Name
            : ViewModel.Status;
        NowPlayingArtwork.ArtworkUrl = station.ArtworkUrl;
        var isFavorite = ViewModel.IsFavorite(station);
        FavoriteNowPlayingIcon.Glyph = isFavorite ? "\uEB52" : "\uEB51";
        AutomationProperties.SetName(FavoriteNowPlayingButton,
            isFavorite ? $"Remove {station.Name} from favorites" : $"Add {station.Name} to favorites");
    }

    private void UpdateStatus()
    {
        if (ViewModel.CurrentStation is not null)
        {
            NowPlayingSubtitle.Text = string.IsNullOrWhiteSpace(ViewModel.Status)
                ? ViewModel.CurrentTrack is null ? ViewModel.CurrentStation.Details : ViewModel.CurrentStation.Name
                : ViewModel.Status;
        }

        var isError = ViewModel.Status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || ViewModel.Status.StartsWith("Unable to play", StringComparison.OrdinalIgnoreCase)
            || ViewModel.Status.Contains("could not be saved", StringComparison.OrdinalIgnoreCase);
        StatusInfoBar.IsOpen = isError;
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.Title = ViewModel.Status.Contains("could not be saved", StringComparison.OrdinalIgnoreCase)
            ? "Library save failed"
            : ViewModel.Status.StartsWith("Unable to play", StringComparison.OrdinalIgnoreCase)
                ? "Playback failed"
                : "Radio directory unavailable";
        StatusInfoBar.Message = isError ? ViewModel.Status : string.Empty;
    }

    private void LibraryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateLibraryVisibility();

    private void UpdateLibraryVisibility()
    {
        RecentSection.Visibility = ViewModel.Recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesSection.Visibility = ViewModel.Favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = ViewModel.Recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeardTracksSection.Visibility = ViewModel.HeardTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryView.Visibility = ViewModel.Favorites.Count == 0 && ViewModel.Recents.Count == 0 && ViewModel.HeardTracks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
