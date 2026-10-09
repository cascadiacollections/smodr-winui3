using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace smodr;

public sealed partial class SoftwareLicensesView : UserControl
{
    public SoftwareLicensesView()
    {
        InitializeComponent();
    }

#pragma warning disable CA1822 // Named controls are generated instance fields from XAML.
    internal void SetNotices(IReadOnlyList<string> sections)
    {
        NoticesList.ItemsSource = sections;
        LoadingRing.IsActive = false;
        LoadingState.Visibility = Visibility.Collapsed;
        NoticesList.Visibility = Visibility.Visible;
    }
#pragma warning restore CA1822
}
