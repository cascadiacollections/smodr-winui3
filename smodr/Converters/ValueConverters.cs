using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Services;

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

public sealed class CachedImageConverter : IValueConverter
{
    private static readonly ImageCacheService _imageCache = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;

        var bitmap = new BitmapImage();
        _ = LoadImageAsync(bitmap, url);
        return bitmap;
    }

    private static async Task LoadImageAsync(BitmapImage bitmap, string url)
    {
        try
        {
            var file = await _imageCache.GetOrDownloadImageAsync(url);
            if (file is not null)
            {
                using var stream = await file.OpenReadAsync();
                await bitmap.SetSourceAsync(stream);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load cached image: {ex.Message}");
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
