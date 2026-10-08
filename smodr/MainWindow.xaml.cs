using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using smodr.Models;
using smodr.Services;
using smodr.ViewModels;
using Windows.System;

namespace smodr;

#pragma warning disable CA1001 // Window.Closed owns cancellation/teardown; this is not a separately disposable service.
public sealed partial class MainWindow : Window
#pragma warning restore CA1001
{
    private AppWindow? _appWindow;
    private bool _closingAfterFlush;
    private bool _closeAfterFlush;
    private bool _closed;
    private readonly bool _settingsReady;
    private bool _licensesOpen;
    private RadioWindowBackdrop? _appliedBackdrop;
    private readonly RadioStreamPrewarmer? _prewarmer;
    private readonly RadioJumpList? _jumpList;
    private readonly CancellationTokenSource _warmupCancellation = new();
    private readonly DispatcherTimer _sleepCountdownTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public RadioMainViewModel ViewModel { get; }
    public RadioSettingsViewModel Settings { get; }

    internal MainWindow(RadioMainViewModel viewModel, RadioSettingsViewModel settings,
        RadioStreamPrewarmer? prewarmer = null, RadioJumpList? jumpList = null)
    {
        ViewModel = viewModel;
        Settings = settings;
        _prewarmer = prewarmer;
        _jumpList = jumpList;
        InitializeComponent();
        ApplyAppearance();
        Settings.PropertyChanged += Settings_PropertyChanged;
        HeroArtwork.AccentColorChanged += HeroArtwork_AccentColorChanged;
        _settingsReady = true;
        _sleepCountdownTimer.Tick += SleepCountdownTimer_Tick;
        _undoTimer.Tick += UndoTimer_Tick;
        UpdateSleepTimer();
        ConfigureWindow();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Recents.CollectionChanged += LibraryCollectionChanged;
        ViewModel.Favorites.CollectionChanged += LibraryCollectionChanged;
        ViewModel.HeardTracks.CollectionChanged += LibraryCollectionChanged;
        ViewModel.TopTracks.CollectionChanged += LibraryCollectionChanged;

        AppNavigation.SelectedItem = ListenNowItem;
        UpdateLibraryVisibility();
        UpdateJumpList();
        Closed += MainWindow_Closed;
        _ = LoadAndWarmAsync();
    }

    private async Task LoadAndWarmAsync()
    {
        try
        {
            await ViewModel.LoadPopularAsync();
            if (_closed) return;
            await Task.Delay(TimeSpan.FromSeconds(5), _warmupCancellation.Token);
            if (_prewarmer is not null && ViewModel.CurrentStation is null)
                await _prewarmer.WarmAsync((RadioStation[])[.. ViewModel.Recents, .. ViewModel.Favorites], _warmupCancellation.Token);
            if (!_closed && ViewModel.PopularStations.Count != 0 && ViewModel.Status.Length == 0)
                await ViewModel.WarmGenresAsync();
        }
        catch (OperationCanceledException) when (_warmupCancellation.IsCancellationRequested) { }
        catch (Exception exception) { AppDiagnostics.Record("window.warmup", exception); }
    }

    public async Task OpenQuickStationAsync(string id)
    {
        if (_closed) return;
        var station = ViewModel.Favorites.Concat(ViewModel.Recents)
            .FirstOrDefault(station => string.Equals(station.Id, id, StringComparison.OrdinalIgnoreCase));
        if (station is null) return;
        AppNavigation.SelectedItem = ListenNowItem;
        await ViewModel.PlaySavedStationAsync(station);
    }

    public async Task OpenStationLinkAsync(StationLaunchLink link)
    {
        if (_closed) return;
        try
        {
            if (link.AutoPlay)
            {
                AppNavigation.SelectedItem = ListenNowItem;
                await ViewModel.PlayStationFromLinkAsync(link.Station);
            }
            else
            {
                AppNavigation.SelectedItem = SearchItem;
                StationSearchBox.Text = link.Station.Name;
                await ViewModel.SearchAsync(link.Station.Name);
            }
        }
        catch (Exception exception) { AppDiagnostics.Record("station.protocol-launch", exception); }
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
        Settings.PropertyChanged -= Settings_PropertyChanged;
        _closed = true;
        _warmupCancellation.Cancel();
        _prewarmer?.Clear();
        _sleepCountdownTimer.Stop();
        _sleepCountdownTimer.Tick -= SleepCountdownTimer_Tick;
        _undoTimer.Stop();
        _undoTimer.Tick -= UndoTimer_Tick;
        _appWindow?.Closing -= AppWindow_Closing;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Recents.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.Favorites.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.HeardTracks.CollectionChanged -= LibraryCollectionChanged;
        ViewModel.TopTracks.CollectionChanged -= LibraryCollectionChanged;
        HeroArtwork.AccentColorChanged -= HeroArtwork_AccentColorChanged;
        ViewModel.Dispose();
        _warmupCancellation.Dispose();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeAfterFlush)
        {
            return;
        }

        args.Cancel = true;
        if (_closingAfterFlush)
        {
            return;
        }

        _closingAfterFlush = true;
        _warmupCancellation.Cancel();
        if (Content is UIElement root) root.IsHitTestVisible = false;
        AppNavigation.IsEnabled = false;
        try
        {
            await Task.WhenAll(ViewModel.ShutdownAsync(), _prewarmer?.ShutdownAsync() ?? Task.CompletedTask,
                StationArtworkControl.ShutdownTransportAsync())
                .WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception exception) { AppDiagnostics.Record("app.shutdown", exception); }
        try
        {
            await FlushPendingAsync().WaitAsync(TimeSpan.FromSeconds(15));
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

    private Task FlushPendingAsync() => Task.WhenAll(ViewModel.FlushLibraryAsync(), ViewModel.FlushDirectoryCacheAsync(),
        ViewModel.FlushPrivacySettingsAsync(), ViewModel.FlushTrackHistoryAsync(), Settings.FlushAsync(),
        _jumpList?.FlushAsync() ?? Task.CompletedTask,
        RuntimeDiagnostics.FlushAsync(Path.Combine(App.StorageDirectory, "runtime-counters.json")));

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

    private async void StationList_ItemClick(object sender, ItemClickEventArgs e) =>
        await PlayStationAsync(e.ClickedItem as RadioStation);

    private void StationList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is StationRowControl row)
            row.SetViewModel(args.InRecycleQueue ? null : ViewModel);
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

    private async void StationRow_FavoriteRequested(object? sender, RadioStation station) =>
        await ViewModel.ToggleFavoriteAsync(station);

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => ViewModel.PlayPause();
    private void RetryPlaybackButton_Click(object sender, RoutedEventArgs e) => ViewModel.RetryPlayback();
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
        if (_settingsReady) await Settings.SetPlayReportingEnabledAsync(PlayReportingSwitch.IsOn);
    }

    private async void AlbumArtworkSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetAlbumArtworkEnabledAsync(AlbumArtworkSwitch.IsOn);
    }

    private async void SoftwareLicenses_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _licensesOpen) return;
        _licensesOpen = true;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var view = new SoftwareLicensesView();
            var dialog = new ContentDialog
            {
                Title = "Software licenses",
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
                Content = view,
            };
            var showing = dialog.ShowAsync();
            _ = PopulateLicenseDialogAsync(view, cancellation.Token);
            await showing;
        }
        catch (Exception exception) { AppDiagnostics.Record("licenses.dialog", exception); }
        finally { cancellation.Cancel(); _licensesOpen = false; }
    }

    private async Task PopulateLicenseDialogAsync(SoftwareLicensesView view, CancellationToken cancellationToken)
    {
        try
        {
            var sections = await Settings.LoadSoftwareLicenseSectionsAsync().WaitAsync(cancellationToken);
            if (!_closed && !cancellationToken.IsCancellationRequested) view.SetNotices(sections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { AppDiagnostics.Record("licenses.dialog-content", exception); }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.CurrentStation):
            case nameof(ViewModel.CurrentTrack):
            case nameof(ViewModel.CurrentArtworkUrl):
            case nameof(ViewModel.CurrentAppleMusicUrl):
                UpdateNowPlaying();
                break;
            case nameof(ViewModel.IsPlaying):
                PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768";
                HeroPlayPauseIcon.Glyph = PlayPauseIcon.Glyph;
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
            case nameof(ViewModel.RemovedFavorite):
                UpdateFavoriteUndo();
                break;
            case nameof(ViewModel.CanRetry):
                UpdateStatus();
                break;
        }
    }

    private void UpdateNowPlaying()
    {
        if (ViewModel.CurrentStation is not { } station)
        {
            MiniPlayer.Visibility = Visibility.Collapsed;
            NowPlayingView.Visibility = Visibility.Collapsed;
            return;
        }

        MiniPlayer.Visibility = Visibility.Visible;
        NowPlayingTitle.Text = ViewModel.CurrentTrack?.Display ?? station.Name;
        NowPlayingSubtitle.Text = string.IsNullOrWhiteSpace(ViewModel.Status)
            ? ViewModel.CurrentTrack is null ? station.Details : station.Name
            : ViewModel.Status;
        NowPlayingArtwork.FallbackArtworkUrl = station.ArtworkUrl;
        NowPlayingArtwork.ArtworkUrl = ViewModel.CurrentArtworkUrl;
        HeroArtwork.FallbackArtworkUrl = station.ArtworkUrl;
        HeroArtwork.ArtworkUrl = ViewModel.CurrentArtworkUrl;
        AmbientArtwork.FallbackArtworkUrl = station.ArtworkUrl;
        AmbientArtwork.ArtworkUrl = ViewModel.CurrentArtworkUrl;
        HeroTitle.Text = NowPlayingTitle.Text;
        HeroSubtitle.Text = station.Name;
        AutomationProperties.SetName(HeroArtwork,
            ViewModel.CurrentTrack is { } track && ViewModel.CurrentArtworkUrl != station.ArtworkUrl
                ? $"Album artwork for {track.Display}"
                : $"Station artwork for {station.Name}");
        AppleMusicLink.Visibility = Uri.TryCreate(ViewModel.CurrentAppleMusicUrl, UriKind.Absolute,
            out var storeUri) ? Visibility.Visible : Visibility.Collapsed;
        AppleMusicLink.NavigateUri = storeUri;
        HeroAppleMusicLink.Visibility = AppleMusicLink.Visibility;
        HeroAppleMusicLink.NavigateUri = storeUri;
        var isFavorite = ViewModel.IsFavorite(station);
        FavoriteNowPlayingIcon.Glyph = isFavorite ? "\uEB52" : "\uEB51";
        HeroFavoriteIcon.Glyph = FavoriteNowPlayingIcon.Glyph;
        AutomationProperties.SetName(FavoriteNowPlayingButton,
            isFavorite ? $"Remove {station.Name} from favorites" : $"Add {station.Name} to favorites");
    }

    private void OpenNowPlaying_Click(object sender, RoutedEventArgs e)
    {
        NowPlayingView.Visibility = Visibility.Visible;
        CloseNowPlayingButton.Focus(FocusState.Programmatic);
    }

    private void CloseNowPlaying_Click(object sender, RoutedEventArgs e) => CloseNowPlaying();

    private void NowPlayingView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        CloseNowPlaying();
        e.Handled = true;
    }

    private void CloseNowPlaying()
    {
        NowPlayingView.Visibility = Visibility.Collapsed;
        OpenNowPlayingButton.Focus(FocusState.Programmatic);
    }

#pragma warning disable CA1822 // Event handler updates the generated instance control.
    private void HeroArtwork_AccentColorChanged(object? sender, Windows.UI.Color? color)
    {
        AmbientTint.Background = color is { } accent
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, accent.R, accent.G, accent.B))
            : null;
    }
#pragma warning restore CA1822

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
        RetryPlaybackButton.Visibility = isError && ViewModel.CanRetry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LibraryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateLibraryVisibility();
        if (ReferenceEquals(sender, ViewModel.Favorites) || ReferenceEquals(sender, ViewModel.Recents)) UpdateJumpList();
    }

    private void UpdateJumpList()
    {
        if (!_closed && _jumpList is not null) _ = _jumpList.UpdateAsync((RadioStation[])[.. ViewModel.Favorites], (RadioStation[])[.. ViewModel.Recents]);
    }

    private async void PrewarmSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetStreamPrewarmingEnabledAsync(PrewarmSwitch.IsOn);
    }

    private async void ResumeSleepSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetResumeAfterSleepEnabledAsync(ResumeSleepSwitch.IsOn);
    }

    private async void ResumeNetworkSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetResumeAfterNetworkLossEnabledAsync(ResumeNetworkSwitch.IsOn);
    }

    private async void LoopBroadcastsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetLoopFinishedBroadcastsEnabledAsync(LoopBroadcastsSwitch.IsOn);
    }

    private async void EqualizerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsReady) await Settings.SetEqualizerPresetAsync(EqualizerBox.SelectedIndex);
    }

    private async void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsReady) await Settings.SetBackdropAsync(BackdropBox.SelectedIndex);
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_closed && args.PropertyName == nameof(Settings.SelectedBackdrop)) ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        var choice = (RadioWindowBackdrop)Settings.SelectedBackdrop;
        if (_appliedBackdrop == choice) return;
        SystemBackdrop = choice switch
        {
            RadioWindowBackdrop.Acrylic => new DesktopAcrylicBackdrop(),
            RadioWindowBackdrop.Mica => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
            _ => null
        };
        SolidWindowBackground.Visibility = choice == RadioWindowBackdrop.Solid ? Visibility.Visible : Visibility.Collapsed;
        _appliedBackdrop = choice;
    }

    private async void JumpListSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsReady) await Settings.SetJumpListEnabledAsync(JumpListSwitch.IsOn);
    }

    private void TopTracksTimeframeBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox comboBox) return;
        var timeframe = comboBox.SelectedIndex switch
        {
            1 => TopTracksTimeframe.Month,
            2 => TopTracksTimeframe.AllTime,
            _ => TopTracksTimeframe.Week
        };
        ViewModel.SetTopTracksTimeframe(timeframe);
    }

    private void UpdateLibraryVisibility()
    {
        if (!_settingsReady) return;
        var section = LibrarySectionBox.SelectedIndex;
        RecentSection.Visibility = ViewModel.Recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesSection.Visibility = section == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistorySection.Visibility = section == 1 ? Visibility.Visible : Visibility.Collapsed;
        TopTracksSection.Visibility = section == 2 ? Visibility.Visible : Visibility.Collapsed;
        HeardTracksSection.Visibility = section == 3 ? Visibility.Visible : Visibility.Collapsed;
        TopTracksEmpty.Visibility = ViewModel.TopTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TopTracksList.Visibility = ViewModel.TopTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryTitle.Text = section switch { 1 => "No Recently Played Stations", 3 => "No Heard Tracks Yet", _ => "No Favorites Yet" };
        var empty = section switch { 0 => ViewModel.Favorites.Count == 0, 1 => ViewModel.Recents.Count == 0, 3 => ViewModel.HeardTracks.Count == 0, _ => false };
        EmptyLibraryView.Visibility = empty
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearRecentsButton.IsEnabled = ViewModel.Recents.Count > 0;
        ClearHeardTracksButton.IsEnabled = ViewModel.HeardTracks.Count > 0;
    }

    private void LibrarySectionBox_SelectionChanged(object sender, SelectionChangedEventArgs args) => UpdateLibraryVisibility();

}
