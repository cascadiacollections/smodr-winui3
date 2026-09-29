using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using smodr.Models;
using smodr.ViewModels;

namespace smodr;

public sealed partial class MainWindow : Window
{
    public RadioMainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Recents.CollectionChanged += LibraryCollectionChanged;
        ViewModel.Favorites.CollectionChanged += LibraryCollectionChanged;

        AppNavigation.SelectedItem = ListenNowItem;
        UpdateLibraryVisibility();
        Closed += MainWindow_Closed;
        _ = ViewModel.LoadPopularAsync();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        ViewModel.Dispose();
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
        UpdateLibraryVisibility();
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

    private void FavoriteNowPlayingButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentStation is { } station)
        {
            ViewModel.ToggleFavorite(station);
            UpdateNowPlaying();
            UpdateLibraryVisibility();
        }
    }

    private void StationFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RadioStation station })
        {
            ToggleFavorite(station);
        }
    }

    private void StationRow_FavoriteRequested(object? sender, RadioStation station) => ToggleFavorite(station);

    private void ToggleFavorite(RadioStation station)
    {
        ViewModel.ToggleFavorite(station);
        UpdateNowPlaying();
        UpdateLibraryVisibility();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => ViewModel.PlayPause();
    private void StopButton_Click(object sender, RoutedEventArgs e) => ViewModel.Stop();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.CurrentStation):
                UpdateNowPlaying();
                break;
            case nameof(ViewModel.IsPlaying):
                PlayPauseIcon.Glyph = ViewModel.IsPlaying ? "\uE769" : "\uE768";
                break;
            case nameof(ViewModel.IsLoading):
                LoadingRing.IsActive = ViewModel.IsLoading;
                LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(ViewModel.Status):
                UpdateStatus();
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
        NowPlayingTitle.Text = station.Name;
        NowPlayingSubtitle.Text = string.IsNullOrWhiteSpace(ViewModel.Status) ? station.Details : ViewModel.Status;
        NowPlayingArtwork.ArtworkUrl = station.ArtworkUrl;
        FavoriteNowPlayingIcon.Glyph = ViewModel.IsFavorite(station) ? "\uEB52" : "\uEB51";
    }

    private void UpdateStatus()
    {
        if (ViewModel.CurrentStation is not null)
        {
            NowPlayingSubtitle.Text = string.IsNullOrWhiteSpace(ViewModel.Status)
                ? ViewModel.CurrentStation.Details
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
        EmptyLibraryView.Visibility = ViewModel.Favorites.Count == 0 && ViewModel.Recents.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
