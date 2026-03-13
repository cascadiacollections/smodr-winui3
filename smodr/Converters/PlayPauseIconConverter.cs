using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

public class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (bool)value ? "⏸" : "▶";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value.ToString() == "⏸";
}
