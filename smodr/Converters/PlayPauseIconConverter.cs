using Microsoft.UI.Xaml.Data;

namespace smodr.Converters;

/// <summary>
/// A value converter that converts a boolean value to a play/pause icon. If the input boolean is true, it returns the pause icon "⏸"; if false, it returns the play icon "▶". This converter can be used in data binding scenarios where a boolean value represents the playback state of an audio or video player.
/// </summary>
public class PlayPauseIconConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to a play/pause icon. If the input boolean is true, it returns the pause icon "⏸"; if false, it returns the play icon "▶".
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>The play or pause icon based on the input boolean value.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (bool)value ? "⏸" : "▶";
    }

    /// <summary>
    /// Converts a play/pause icon back to a boolean value. If the input icon is the pause icon "⏸", it returns true; if the input icon is the play icon "▶", it returns false.
    /// </summary>
    /// <param name="value">The play/pause icon to convert.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="language">The language of the conversion.</param>
    /// <returns>True if the input icon is the pause icon "⏸"; otherwise, false.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value.ToString() == "⏸";
    }
}
