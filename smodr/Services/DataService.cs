using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using smodr.Models;

namespace smodr.Services
{
    public class DataService
    {
        private readonly HttpClient _httpClient;
        private readonly CacheService _cacheService;
        private const string SMODCAST_RSS_URL = "https://feeds.feedburner.com/SModcasts";

        public DataService()
        {
            _httpClient = new HttpClient();
            _cacheService = new CacheService();
        }

        public async Task<List<Episode>> GetEpisodesAsync(bool forceRefresh = false)
        {
            try
            {
                // Initialize cache service
                await _cacheService.InitializeAsync();

                // Try to get cached episodes first (unless force refresh is requested)
                if (!forceRefresh)
                {
                    var cachedEpisodes = await _cacheService.GetCachedEpisodesAsync();
                    if (cachedEpisodes != null && cachedEpisodes.Count > 0)
                    {
                        Debug.WriteLine($"Using cached episodes: {cachedEpisodes.Count} items");
                        return cachedEpisodes;
                    }
                }

                // Fetch fresh data from RSS feed
                Debug.WriteLine("Fetching fresh episodes from RSS feed...");
                var episodes = await FetchEpisodesFromRssAsync();

                // Cache the fresh data
                if (episodes.Count > 0)
                {
                    await _cacheService.CacheEpisodesAsync(episodes);
                }

                return episodes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetEpisodesAsync: {ex.Message}");
                
                // If network fails, try to fallback to cached data (even if expired)
                try
                {
                    var fallbackEpisodes = await _cacheService.GetCachedEpisodesAsync();
                    if (fallbackEpisodes != null && fallbackEpisodes.Count > 0)
                    {
                        Debug.WriteLine("Using expired cached episodes as fallback");
                        return fallbackEpisodes;
                    }
                }
                catch
                {
                    // Ignore cache errors when already in error state
                }

                return new List<Episode>();
            }
        }

        private async Task<List<Episode>> FetchEpisodesFromRssAsync()
        {
            var response = await _httpClient.GetAsync(SMODCAST_RSS_URL);
            response.EnsureSuccessStatusCode();
            
            var rssContent = await response.Content.ReadAsStringAsync();
            
            using var xmlReader = XmlReader.Create(new StringReader(rssContent));
            var feed = SyndicationFeed.Load(xmlReader);
            
            var episodes = new List<Episode>();
            
            foreach (var item in feed.Items)
            {
                var episode = new Episode
                {
                    Title = item.Title?.Text ?? "Unknown Title",
                    Description = GetDescription(item),
                    PublishDate = item.PublishDate.DateTime,
                    MediaUrl = GetMediaUrl(item),
                    ImageUrl = GetImageUrl(item),
                    Duration = GetDuration(item),
                    FileSize = GetFileSize(item),
                    EpisodeNumber = ExtractEpisodeNumber(item.Title?.Text ?? "")
                };
                
                episodes.Add(episode);
            }
            
            return episodes.OrderByDescending(e => e.PublishDate).ToList();
        }

        public async Task<bool> ClearCacheAsync()
        {
            await _cacheService.InitializeAsync();
            return await _cacheService.ClearCacheAsync();
        }

        public async Task<CacheMetadata?> GetCacheInfoAsync()
        {
            await _cacheService.InitializeAsync();
            return await _cacheService.GetCacheMetadataAsync();
        }

        public async Task<long> GetCacheSizeAsync()
        {
            await _cacheService.InitializeAsync();
            return await _cacheService.GetCacheSizeAsync();
        }

        private string GetDescription(SyndicationItem item)
        {
            if (item.Summary?.Text != null)
                return item.Summary.Text;
            
            if (item.Content is TextSyndicationContent textContent)
                return textContent.Text;
            
            return "No description available";
        }

        private string GetMediaUrl(SyndicationItem item)
        {
            var enclosure = item.Links?.FirstOrDefault(l => l.RelationshipType == "enclosure");
            if (enclosure != null)
                return enclosure.Uri.ToString();
            
            // Try to find media in item extensions
            foreach (var extension in item.ElementExtensions)
            {
                if (extension.OuterName == "enclosure")
                {
                    var reader = extension.GetReader();
                    while (reader.Read())
                    {
                        if (reader.HasAttributes)
                        {
                            var url = reader.GetAttribute("url");
                            if (!string.IsNullOrEmpty(url))
                                return url;
                        }
                    }
                }
            }
            
            return string.Empty;
        }

        private string GetImageUrl(SyndicationItem item)
        {
            // Try to get image from iTunes extensions
            foreach (var extension in item.ElementExtensions)
            {
                if (extension.OuterName == "image" && extension.OuterNamespace.Contains("itunes"))
                {
                    var reader = extension.GetReader();
                    return reader.GetAttribute("href") ?? string.Empty;
                }
            }
            
            // Fallback to default Smodcast image
            return "http://smodcast.com/wp-content/blogs.dir/1/files_mf/smodcast1400.jpg";
        }

        private string GetDuration(SyndicationItem item)
        {
            foreach (var extension in item.ElementExtensions)
            {
                if (extension.OuterName == "duration" && extension.OuterNamespace.Contains("itunes"))
                {
                    return extension.GetObject<string>();
                }
            }
            return string.Empty;
        }

        private long GetFileSize(SyndicationItem item)
        {
            var enclosure = item.Links?.FirstOrDefault(l => l.RelationshipType == "enclosure");
            if (enclosure != null && long.TryParse(enclosure.Length.ToString(), out var size))
            {
                return size;
            }
            return 0;
        }

        private string ExtractEpisodeNumber(string title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;

            // Try to extract episode number from title (e.g., "SModcast #123")
            var parts = title.Split('#');
            if (parts.Length > 1)
            {
                var numberPart = parts[1].Split(' ')[0];
                return numberPart;
            }
            
            return string.Empty;
        }

        public async Task<List<Episode>?> GetCachedEpisodesAsync()
        {
            try
            {
                await _cacheService.InitializeAsync();
                return await _cacheService.GetCachedEpisodesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting cached episodes: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}