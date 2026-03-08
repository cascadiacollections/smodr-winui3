using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using smodr.Models;
using Windows.Storage;

namespace smodr.Services;

public sealed class ImageCacheService
{
    private const string ImageCacheFolderName = "ImageCache";
    private static readonly HttpClient _httpClient = new();
    private readonly PodcastDirectoryService _directoryService = new();

    private StorageFolder? _cacheFolder;

    static ImageCacheService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "smodr/1.0 (+https://github.com/cascadiacollections/smodr-winui3)");
    }

    private async Task EnsureInitializedAsync()
    {
        _cacheFolder ??= await ApplicationData.Current.LocalFolder
            .CreateFolderAsync(ImageCacheFolderName, CreationCollisionOption.OpenIfExists);
    }

    /// <summary>
    /// Resolves the best artwork for a podcast: iTunes Lookup API → fallback ImageUrl.
    /// Downloads and caches the image locally.
    /// </summary>
    public async Task<StorageFile?> GetPodcastArtworkAsync(Podcast podcast)
    {
        try
        {
            // Try iTunes artwork first (always fresh from Apple CDN)
            if (podcast.ApplePodcastId is { } appleId)
            {
                var artworkUrl = await _directoryService.GetArtworkUrlAsync(appleId);
                if (!string.IsNullOrEmpty(artworkUrl))
                {
                    var file = await GetOrDownloadImageAsync(artworkUrl);
                    if (file is not null)
                        return file;
                }
            }

            // Fall back to the static ImageUrl
            if (!string.IsNullOrEmpty(podcast.ImageUrl))
            {
                return await GetOrDownloadImageAsync(podcast.ImageUrl);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get podcast artwork for '{podcast.Name}': {ex.Message}");
        }

        return null;
    }

    public async Task<StorageFile?> GetOrDownloadImageAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return null;

        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return null;

            var fileName = GetCacheFileName(imageUrl);

            if (await _cacheFolder.TryGetItemAsync(fileName) is StorageFile cachedFile)
            {
                Debug.WriteLine($"Image cache hit: {fileName}");
                return cachedFile;
            }

            Debug.WriteLine($"Downloading image: {imageUrl}");
            var bytes = await _httpClient.GetByteArrayAsync(new Uri(imageUrl));

            var file = await _cacheFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteBytesAsync(file, bytes);

            Debug.WriteLine($"Image cached: {fileName} ({bytes.Length} bytes)");
            return file;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get/download image '{imageUrl}': {ex.Message}");
            return null;
        }
    }

    private static string GetCacheFileName(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(extension))
            extension = ".img";
        return $"{hash}{extension}";
    }
}
