using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using smodr.Models;

namespace smodr.Services
{
    public class CacheService
    {
        private const string CACHE_FOLDER_NAME = "EpisodeCache";
        private const string EPISODES_CACHE_FILE = "episodes.json";
        private const string CACHE_METADATA_FILE = "cache_metadata.json";
        // Cache expiry time is now configurable via application settings (LocalSettings["CacheExpiryHours"])
        private const int DEFAULT_CACHE_EXPIRY_HOURS = 6;
        private int CacheExpiryHours
        {
            get
            {
                object? value = ApplicationData.Current.LocalSettings.Values["CacheExpiryHours"];
                if (value is int intValue)
                {
                    return intValue;
                }
                else if (value is string strValue && int.TryParse(strValue, out int parsed))
                {
                    return parsed;
                }
                return DEFAULT_CACHE_EXPIRY_HOURS;
            }
        }

        private StorageFolder? _cacheFolder;

        public async Task InitializeAsync()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                _cacheFolder = await localFolder.CreateFolderAsync(CACHE_FOLDER_NAME, CreationCollisionOption.OpenIfExists);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing cache folder: {ex.Message}");
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
                var episodesFile = await _cacheFolder.TryGetItemAsync(EPISODES_CACHE_FILE) as StorageFile;
                if (episodesFile == null)
                    return null;

                var jsonContent = await FileIO.ReadTextAsync(episodesFile);
                var episodes = JsonSerializer.Deserialize<List<Episode>>(jsonContent);

                System.Diagnostics.Debug.WriteLine($"Loaded {episodes?.Count ?? 0} episodes from cache");
                return episodes;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading cached episodes: {ex.Message}");
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
                var episodesFile = await _cacheFolder.CreateFileAsync(EPISODES_CACHE_FILE, CreationCollisionOption.ReplaceExisting);
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

                var metadataFile = await _cacheFolder.CreateFileAsync(CACHE_METADATA_FILE, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(metadataFile, metadataJson);

                System.Diagnostics.Debug.WriteLine($"Cached {episodes.Count} episodes successfully");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error caching episodes: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsCacheValidAsync()
        {
            try
            {
                if (_cacheFolder == null)
                    return false;

                var metadataFile = await _cacheFolder.TryGetItemAsync(CACHE_METADATA_FILE) as StorageFile;
                if (metadataFile == null)
                    return false;

                var metadataJson = await FileIO.ReadTextAsync(metadataFile);
                var metadata = JsonSerializer.Deserialize<CacheMetadata>(metadataJson);

                if (metadata == null)
                    return false;

                var timeSinceLastUpdate = DateTime.UtcNow - metadata.LastUpdated;
                var isValid = timeSinceLastUpdate.TotalHours < CACHE_EXPIRY_HOURS;

                System.Diagnostics.Debug.WriteLine($"Cache age: {timeSinceLastUpdate.TotalHours:F1} hours, Valid: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking cache validity: {ex.Message}");
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

                var metadataFile = await _cacheFolder.TryGetItemAsync(CACHE_METADATA_FILE) as StorageFile;
                if (metadataFile == null)
                    return null;

                var metadataJson = await FileIO.ReadTextAsync(metadataFile);
                return JsonSerializer.Deserialize<CacheMetadata>(metadataJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading cache metadata: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine("Cache cleared successfully");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing cache: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"Error calculating cache size: {ex.Message}");
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