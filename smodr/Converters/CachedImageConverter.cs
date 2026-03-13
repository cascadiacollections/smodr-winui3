using System.Diagnostics;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Services;

namespace smodr.Converters;

public sealed class CachedImageConverter : IValueConverter
{
    private static readonly ImageCacheService _imageCache = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        _ = LoadImageAsync(bitmap, url);
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

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
}
