using System.Diagnostics;
using System.ServiceModel.Syndication;
using System.Xml;
using smodr.Models;

namespace smodr.Services;

public class DataService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly CacheService _cacheService = new();
    private const string SmodcastRssUrl = "https://feeds.feedburner.com/SModcasts";

    public async Task<List<Episode>> GetEpisodesAsync(bool forceRefresh = false)
    {
        try
        {
            await _cacheService.InitializeAsync();

            if (!forceRefresh)
            {
                var cachedEpisodes = await _cacheService.GetCachedEpisodesAsync();
                if (cachedEpisodes is { Count: > 0 })
                {
                    Debug.WriteLine($"Using cached episodes: {cachedEpisodes.Count} items");
                    return cachedEpisodes;
                }
            }

            Debug.WriteLine("Fetching fresh episodes from RSS feed...");
            var episodes = await FetchEpisodesFromRssAsync();

            if (episodes.Count > 0)
            {
                await _cacheService.CacheEpisodesAsync(episodes);
            }

            return episodes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetEpisodesAsync: {ex.Message}");

            try
            {
                var fallbackEpisodes = await _cacheService.GetCachedEpisodesAsync();
                if (fallbackEpisodes is { Count: > 0 })
                {
                    Debug.WriteLine("Using expired cached episodes as fallback");
                    return fallbackEpisodes;
                }
            }
            catch
            {
                // Ignore cache errors when already in error state
            }

            return [];
        }
    }

    private async Task<List<Episode>> FetchEpisodesFromRssAsync()
    {
        var response = await _httpClient.GetAsync(SmodcastRssUrl);
        response.EnsureSuccessStatusCode();

        var rssContent = await response.Content.ReadAsStringAsync();

        using var xmlReader = XmlReader.Create(new StringReader(rssContent));
        var feed = SyndicationFeed.Load(xmlReader);

        var episodes = feed.Items.Select(item => new Episode
        {
            Title = item.Title?.Text ?? "Unknown Title",
            Description = GetDescription(item),
            PublishDate = item.PublishDate.DateTime,
            MediaUrl = GetMediaUrl(item),
            ImageUrl = GetImageUrl(item),
            Duration = GetDuration(item),
            FileSize = GetFileSize(item),
            EpisodeNumber = ExtractEpisodeNumber(item.Title?.Text ?? "")
        });

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

    private static string GetDescription(SyndicationItem item)
    {
        if (item.Summary?.Text is { } summary)
            return summary;

        if (item.Content is TextSyndicationContent textContent)
            return textContent.Text;

        return "No description available";
    }

    private static string GetMediaUrl(SyndicationItem item)
    {
        var enclosure = item.Links?.FirstOrDefault(l => l.RelationshipType == "enclosure");
        if (enclosure is not null)
            return enclosure.Uri.ToString();

        foreach (var extension in item.ElementExtensions)
        {
            if (extension.OuterName is not "enclosure")
                continue;

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

        return string.Empty;
    }

    private static string GetImageUrl(SyndicationItem item)
    {
        foreach (var extension in item.ElementExtensions)
        {
            if (extension.OuterName == "image" && extension.OuterNamespace.Contains("itunes"))
            {
                var reader = extension.GetReader();
                return reader.GetAttribute("href") ?? string.Empty;
            }
        }

        return "http://smodcast.com/wp-content/blogs.dir/1/files_mf/smodcast1400.jpg";
    }

    private static string GetDuration(SyndicationItem item)
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

    private static long GetFileSize(SyndicationItem item)
    {
        var enclosure = item.Links?.FirstOrDefault(l => l.RelationshipType == "enclosure");
        return enclosure is not null && long.TryParse(enclosure.Length.ToString(), out var size) ? size : 0;
    }

    private static string ExtractEpisodeNumber(string title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;

        var parts = title.Split('#');
        return parts.Length > 1 ? parts[1].Split(' ')[0] : string.Empty;
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

    public void Dispose() => _httpClient?.Dispose();
}
