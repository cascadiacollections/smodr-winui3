using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

/// <summary>
/// A value converter that negates a boolean value. This converter can be used in data binding scenarios where a boolean value needs to be inverted, such as toggling visibility or enabling/disabling UI elements based on a condition.
/// </summary>
public class BoolNegationConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to its negated equivalent. If the input value is true, it returns false; if the input value is false, it returns true.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>The negated boolean value.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !(bool)value;
    }

    /// <summary>
    /// Converts a negated boolean value back to its original equivalent. If the input value is true, it returns false; if the input value is false, it returns true.
    /// </summary>
    /// <param name="value">The boolean value to convert back.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns></returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return !(bool)value;
    }
}
