using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

public partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        if (parameter?.ToString() == "True")
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (Visibility)value == Visibility.Visible;
}
