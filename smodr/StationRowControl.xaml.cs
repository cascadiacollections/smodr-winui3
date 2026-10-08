using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using smodr.Models;
using smodr.ViewModels;
using Windows.Media.Playback;

namespace smodr;

public sealed partial class StationRowControl : UserControl
{
    public static readonly DependencyProperty StationProperty = DependencyProperty.Register(
        nameof(Station),
        typeof(RadioStation),
        typeof(StationRowControl),
        new PropertyMetadata(new RadioStation(), (sender, _) => ((StationRowControl)sender).UpdateState()));

    private RadioMainViewModel? _viewModel;
    private bool _observing;

    public StationRowControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { Observe(); UpdateState(); };
        Unloaded += (_, _) => Unobserve();
    }

    public RadioStation Station
    {
        get => (RadioStation)GetValue(StationProperty);
        set => SetValue(StationProperty, value);
    }

    public static readonly DependencyProperty IsReorderableProperty = DependencyProperty.Register(
        nameof(IsReorderable),
        typeof(bool),
        typeof(StationRowControl),
        new PropertyMetadata(false, (sender, args) => ((StationRowControl)sender).UpdateReorderMenu((bool)args.NewValue)));

    /// <summary>Shows Move up/down commands, the non-drag alternative for keyboard, touch and screen-reader users.</summary>
    public bool IsReorderable
    {
        get => (bool)GetValue(IsReorderableProperty);
        set => SetValue(IsReorderableProperty, value);
    }

    public event EventHandler<RadioStation>? FavoriteRequested;

    /// <summary>Raised with -1 (up) or 1 (down).</summary>
    public event EventHandler<int>? MoveRequested;

    private void UpdateReorderMenu(bool reorderable)
    {
        if (MoveUpItem is null) return;
        MoveUpItem.Visibility = MoveDownItem.Visibility = reorderable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string tag } && int.TryParse(tag, out var offset))
            MoveRequested?.Invoke(this, offset);
    }

    internal void SetViewModel(RadioMainViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _viewModel)) return;
        Unobserve();
        _viewModel = viewModel;
        if (IsLoaded) Observe();
        UpdateState();
    }

    private void Observe()
    {
        if (_observing || _viewModel is null) return;
        _viewModel.PropertyChanged += StateChanged;
        _viewModel.Favorites.CollectionChanged += FavoritesChanged;
        _observing = true;
    }

    private void Unobserve()
    {
        if (!_observing || _viewModel is null) return;
        _viewModel.PropertyChanged -= StateChanged;
        _viewModel.Favorites.CollectionChanged -= FavoritesChanged;
        _observing = false;
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RadioMainViewModel.CurrentStation) or nameof(RadioMainViewModel.CurrentPlaybackState)
            or nameof(RadioMainViewModel.IsReconnecting)) UpdateState();
    }

    private void FavoritesChanged(object? sender, NotifyCollectionChangedEventArgs args) => UpdateState();

    private void UpdateState()
    {
        if (RowRoot is null) return; // A dependency-property change can precede XAML initialization.
        var favorite = _viewModel?.IsFavorite(Station) == true;
        var active = _viewModel?.CurrentStation is { } current && RadioStationIdentity.Matches(current, Station);
        var state = active ? _viewModel!.CurrentPlaybackState : MediaPlaybackState.None;
        var label = state switch
        {
            MediaPlaybackState.Opening or MediaPlaybackState.Buffering when _viewModel!.IsReconnecting => "Reconnecting…",
            MediaPlaybackState.Playing => "Playing",
            MediaPlaybackState.Buffering => "Buffering…",
            MediaPlaybackState.Opening => "Loading…",
            MediaPlaybackState.Paused => "Paused",
            _ => string.Empty
        };
        PlaybackLabel.Text = label;
        PlaybackLabel.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaybackIcon.Glyph = state is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening ? "\uE769" : "\uE768";
        FavoriteIcon.Glyph = favorite ? "\uEB52" : "\uEB51";
        var favoriteAction = favorite ? "Remove favorite" : "Add favorite";
        AutomationProperties.SetName(FavoriteButton, $"{favoriteAction}: {Station.Name}");
        ToolTipService.SetToolTip(FavoriteButton, favoriteAction);
        AutomationProperties.SetName(RowRoot, string.Join(" · ", new[] { Station.Name, Station.Details, label, favorite ? "Favorite" : string.Empty }.Where(value => value.Length > 0)));
    }

    private void FavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        FavoriteRequested?.Invoke(this, Station);
    }
}
