using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = (bool)value;
        if (parameter?.ToString() == "True")
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        (Visibility)value == Visibility.Visible;
}

public class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        !(bool)value;
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("ConvertBack is not supported for StringToVisibilityConverter.");
}

public class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (bool)value ? "⏸" : "▶";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value?.ToString() == "⏸";
}
