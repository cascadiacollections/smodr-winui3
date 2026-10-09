using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

/// <summary>
///     A value converter that converts a boolean value to a Visibility value. If the input boolean is true, it returns
///     Visibility.Visible; if false, it returns Visibility.Collapsed. An optional parameter can be provided to invert the
///     boolean logic.
/// </summary>
public partial class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    ///     Converts a boolean value to a Visibility value. If the input boolean is true, it returns Visibility.Visible; if
    ///     false, it returns Visibility.Collapsed. An optional parameter can be provided to invert the boolean logic.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>A Visibility value based on the input boolean and the optional parameter.</returns>
    public object Convert(object value, Type targetType, object? parameter, string language)
    {
        var boolValue = (bool)value;
        if (parameter?.ToString() == "True")
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    ///     Converts a Visibility value back to a boolean value. If the input Visibility is Visibility.Visible, it returns
    ///     true; if Visibility.Collapsed, it returns false. An optional parameter can be provided to invert the boolean logic.
    /// </summary>
    /// <param name="value">The Visibility value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns></returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return (Visibility)value == Visibility.Visible;
    }
}
