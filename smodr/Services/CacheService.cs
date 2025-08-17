using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using smodr.Models;

namespace smodr.Services
{
    public class CacheService
    {
        private const string CacheFolderName = "EpisodeCache";
        private const string EpisodesCacheFile = "episodes.json";
        private const string CacheMetadataFile = "cache_metadata.json";
        private const int DefaultCacheExpiryHours = 6;

        private static int CacheExpiryHours
        {
            get
            {
                var value = ApplicationData.Current.LocalSettings.Values["CacheExpiryHours"];
                return value switch
                {
                    int intValue => intValue,
                    string strValue when int.TryParse(strValue, out int parsed) => parsed,
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

        public async Task<List<Episode>?> GetCachedEpisodesAsync()
        {
            try
            {
                if (_cacheFolder == null)
                    await InitializeAsync();

                if (_cacheFolder == null)
                    return null;

                // Check if cache is valid
                if (!await IsCacheValidAsync())
                    return null;

                // Load cached episodes
                var episodesFile = await _cacheFolder.TryGetItemAsync(EpisodesCacheFile) as StorageFile;
                if (episodesFile == null)
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

        public async Task<bool> CacheEpisodesAsync(List<Episode> episodes)
        {
            try
            {
                if (_cacheFolder == null)
                    await InitializeAsync();

                if (_cacheFolder == null)
                    return false;

                // Serialize episodes to JSON
                var jsonContent = JsonSerializer.Serialize(episodes, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                // Save episodes to cache file
                var episodesFile = await _cacheFolder.CreateFileAsync(EpisodesCacheFile, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(episodesFile, jsonContent);

                // Save cache metadata
                var metadata = new CacheMetadata
                {
                    LastUpdated = DateTime.UtcNow,
                    EpisodeCount = episodes.Count
                };

                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var metadataFile = await _cacheFolder.CreateFileAsync(CacheMetadataFile, CreationCollisionOption.ReplaceExisting);
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

        public async Task<bool> IsCacheValidAsync()
        {
            try
            {
                if (_cacheFolder == null)
                    return false;

                var metadataFile = await _cacheFolder.TryGetItemAsync(CacheMetadataFile) as StorageFile;
                if (metadataFile == null)
                    return false;

                var metadataJson = await FileIO.ReadTextAsync(metadataFile);
                var metadata = JsonSerializer.Deserialize<CacheMetadata>(metadataJson);

                if (metadata == null)
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

        public async Task<CacheMetadata?> GetCacheMetadataAsync()
        {
            try
            {
                if (_cacheFolder == null)
                    await InitializeAsync();

                if (_cacheFolder == null)
                    return null;

                var metadataFile = await _cacheFolder.TryGetItemAsync(CacheMetadataFile) as StorageFile;
                if (metadataFile == null)
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
                if (_cacheFolder == null)
                    await InitializeAsync();

                if (_cacheFolder == null)
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
                if (_cacheFolder == null)
                    await InitializeAsync();

                if (_cacheFolder == null)
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
        public DateTime LastUpdated { get; set; }
        public int EpisodeCount { get; set; }
        public string Version { get; set; } = "1.0";
    }
}