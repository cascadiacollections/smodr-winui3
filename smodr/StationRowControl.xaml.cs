using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using smodr.Models;

namespace smodr;

public sealed partial class StationRowControl : UserControl
{
    public static readonly DependencyProperty StationProperty = DependencyProperty.Register(
        nameof(Station),
        typeof(RadioStation),
        typeof(StationRowControl),
        new PropertyMetadata(new RadioStation()));

    public StationRowControl()
    {
        InitializeComponent();
    }

    public RadioStation Station
    {
        get => (RadioStation)GetValue(StationProperty);
        set => SetValue(StationProperty, value);
    }

    public event EventHandler<RadioStation>? FavoriteRequested;

    private void FavoriteMenuItem_Click(object sender, RoutedEventArgs e) =>
        FavoriteRequested?.Invoke(this, Station);
}
