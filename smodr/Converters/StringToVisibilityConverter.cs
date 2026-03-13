using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value.ToString()) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("ConvertBack is not supported for StringToVisibilityConverter.");
}
