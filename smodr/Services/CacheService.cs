using System.Diagnostics;
using System.Text.Json;
using smodr.Models;
using Windows.Storage;

namespace smodr.Services;

public class CacheService
{
    private const string CacheFolderName = "EpisodeCache";
    private const int DefaultCacheExpiryHours = 6;

    private static string EpisodesCacheFileName(string podcastId) => $"{podcastId}_episodes.json";
    private static string MetadataCacheFileName(string podcastId) => $"{podcastId}_metadata.json";

    private int CacheExpiryHours
    {
        get
        {
            var value = ApplicationData.Current.LocalSettings.Values["CacheExpiryHours"];
            return value switch
            {
                int intValue => intValue,
                string strValue when int.TryParse(strValue, out var parsed) => parsed,
                _ => DefaultCacheExpiryHours
            };
        }
    }

    private StorageFolder? _cacheFolder;

    public async Task InitializeAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            _cacheFolder = await localFolder.CreateFolderAsync(CacheFolderName, CreationCollisionOption.OpenIfExists);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing cache folder: {ex.Message}");
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_cacheFolder is null)
            await InitializeAsync();
    }

    public async Task<List<Episode>?> GetCachedEpisodesAsync(string podcastId)
    {
        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return null;

            if (!await IsCacheValidAsync(podcastId))
                return null;

            if (await _cacheFolder.TryGetItemAsync(EpisodesCacheFileName(podcastId)) is not StorageFile episodesFile)
                return null;

            var jsonContent = await FileIO.ReadTextAsync(episodesFile);
            var episodes = JsonSerializer.Deserialize<List<Episode>>(jsonContent);

            Debug.WriteLine($"Loaded {episodes?.Count ?? 0} episodes from cache");
            return episodes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading cached episodes: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> CacheEpisodesAsync(string podcastId, List<Episode> episodes, string? etag = null, DateTimeOffset? lastModified = null)
    {
        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return false;

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            var jsonContent = JsonSerializer.Serialize(episodes, jsonOptions);
            var episodesFile = await _cacheFolder.CreateFileAsync(EpisodesCacheFileName(podcastId), CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(episodesFile, jsonContent);

            var metadata = new CacheMetadata
            {
                LastUpdated = DateTime.UtcNow,
                EpisodeCount = episodes.Count,
                ETag = etag,
                LastModified = lastModified
            };

            var metadataJson = JsonSerializer.Serialize(metadata, jsonOptions);
            var metadataFile = await _cacheFolder.CreateFileAsync(MetadataCacheFileName(podcastId), CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(metadataFile, metadataJson);

            Debug.WriteLine($"Cached {episodes.Count} episodes successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error caching episodes: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsCacheValidAsync(string podcastId)
    {
        try
        {
            if (_cacheFolder is null)
                return false;

            if (await _cacheFolder.TryGetItemAsync(MetadataCacheFileName(podcastId)) is not StorageFile metadataFile)
                return false;

            var metadataJson = await FileIO.ReadTextAsync(metadataFile);
            var metadata = JsonSerializer.Deserialize<CacheMetadata>(metadataJson);

            if (metadata is null)
                return false;

            var timeSinceLastUpdate = DateTime.UtcNow - metadata.LastUpdated;
            var isValid = timeSinceLastUpdate.TotalHours < CacheExpiryHours;

            Debug.WriteLine($"Cache age: {timeSinceLastUpdate.TotalHours:F1} hours, Valid: {isValid}");
            return isValid;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking cache validity: {ex.Message}");
            return false;
        }
    }

    public async Task<CacheMetadata?> GetCacheMetadataAsync(string podcastId)
    {
        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return null;

            if (await _cacheFolder.TryGetItemAsync(MetadataCacheFileName(podcastId)) is not StorageFile metadataFile)
                return null;

            var metadataJson = await FileIO.ReadTextAsync(metadataFile);
            return JsonSerializer.Deserialize<CacheMetadata>(metadataJson);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading cache metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ClearCacheAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return false;

            var files = await _cacheFolder.GetFilesAsync();
            foreach (var file in files)
            {
                await file.DeleteAsync();
            }

            Debug.WriteLine("Cache cleared successfully");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error clearing cache: {ex.Message}");
            return false;
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
                return 0;

            long totalSize = 0;
            var files = await _cacheFolder.GetFilesAsync();

            foreach (var file in files)
            {
                var properties = await file.GetBasicPropertiesAsync();
                totalSize += (long)properties.Size;
            }

            return totalSize;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calculating cache size: {ex.Message}");
            return 0;
        }
    }
}

public class CacheMetadata
{
    public DateTime LastUpdated { get; init; }
    public int EpisodeCount { get; init; }
    public string Version { get; init; } = "1.0";
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}
