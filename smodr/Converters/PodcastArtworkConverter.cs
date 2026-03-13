using System.Diagnostics;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Models;
using smodr.Services;

namespace smodr.Converters;

/// <summary>
///     Resolves podcast artwork via iTunes Lookup API with local caching,
///     falling back to the static <see cref="Podcast.ImageUrl" />.
///     Bind to the <see cref="Podcast" /> object (no path).
/// </summary>
public sealed class PodcastArtworkConverter : IValueConverter
{
    private static readonly ImageCacheService _imageCache = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not Podcast podcast)
        {
            return null;
        }

        var bitmap = new BitmapImage();
        _ = LoadArtworkAsync(bitmap, podcast);
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static async Task LoadArtworkAsync(BitmapImage bitmap, Podcast podcast)
    {
        try
        {
            var file = await _imageCache.GetPodcastArtworkAsync(podcast);
            if (file is not null)
            {
                using var stream = await file.OpenReadAsync();
                await bitmap.SetSourceAsync(stream);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load podcast artwork: {ex.Message}");
        }
    }
}
