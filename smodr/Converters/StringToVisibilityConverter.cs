using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

/// <summary>
/// A value converter that converts a string value to a Visibility value. If the input string is null or empty, it returns Visibility.Collapsed; otherwise, it returns Visibility.Visible. This converter can be used in data binding scenarios where the visibility of a UI element depends on the presence of a string value.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a string value to a Visibility value. If the input string is null or empty, it returns Visibility.Collapsed; otherwise, it returns Visibility.Visible. This converter can be used in data binding scenarios where the visibility of a UI element depends on the presence of a string value.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>A Visibility value based on the input string.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return string.IsNullOrEmpty(value.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Converts a Visibility value back to a string value. This operation is not supported and will throw a NotSupportedException.
    /// </summary>
    /// <param name="value">The Visibility value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>Nothing. This method always throws a NotSupportedException.</returns>
    /// <exception cref="NotSupportedException"></exception>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException("ConvertBack is not supported for StringToVisibilityConverter.");
    }
}
