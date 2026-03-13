using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

public class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        !(bool)value;
}
