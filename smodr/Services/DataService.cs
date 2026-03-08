using System.Diagnostics;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using smodr.Models;

namespace smodr.Services;

public class DataService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly CacheService _cacheService = new();
    private const string UserAgent = "smodr/1.0 (+https://github.com/cascadiacollections/smodr-winui3)";

    public async Task<List<Episode>> GetEpisodesAsync(string podcastId, string feedUrl, bool forceRefresh = false)
    {
        try
        {
            await _cacheService.InitializeAsync();

            if (!forceRefresh)
            {
                var cachedEpisodes = await _cacheService.GetCachedEpisodesAsync(podcastId);
                if (cachedEpisodes is { Count: > 0 })
                {
                    Debug.WriteLine($"Using cached episodes for {podcastId}: {cachedEpisodes.Count} items");
                    return cachedEpisodes;
                }
            }

            Debug.WriteLine($"Fetching fresh episodes for {podcastId} from {feedUrl}...");
            var (episodes, etag, lastModified) = await FetchEpisodesFromRssAsync(podcastId, feedUrl);

            if (episodes.Count > 0)
            {
                await _cacheService.CacheEpisodesAsync(podcastId, episodes, etag, lastModified);
            }

            return episodes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetEpisodesAsync for {podcastId}: {ex.Message}");

            try
            {
                var fallbackEpisodes = await _cacheService.GetCachedEpisodesAsync(podcastId);
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

    private async Task<(List<Episode> Episodes, string? ETag, DateTimeOffset? LastModified)> FetchEpisodesFromRssAsync(string podcastId, string feedUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        // Use conditional requests to respect server caching (ETag / Last-Modified)
        var metadata = await _cacheService.GetCacheMetadataAsync(podcastId);
        if (metadata?.ETag is { Length: > 0 } etag)
        {
            request.Headers.IfNoneMatch.ParseAdd(etag);
        }

        if (metadata?.LastModified is { } lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            Debug.WriteLine($"RSS feed for {podcastId} not modified (304), using cache");
            var cached = await _cacheService.GetCachedEpisodesAsync(podcastId);
            return (cached ?? [], metadata?.ETag, metadata?.LastModified);
        }

        response.EnsureSuccessStatusCode();

        // Stream directly into the XML reader instead of buffering the full string
        using var stream = await response.Content.ReadAsStreamAsync();
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit });
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

        var newETag = response.Headers.ETag?.Tag;
        var newLastModified = response.Content.Headers.LastModified;

        return (episodes.OrderByDescending(e => e.PublishDate).ToList(), newETag, newLastModified);
    }

    public async Task<bool> ClearCacheAsync()
    {
        await _cacheService.InitializeAsync();
        return await _cacheService.ClearCacheAsync();
    }

    public async Task<CacheMetadata?> GetCacheInfoAsync(string podcastId)
    {
        await _cacheService.InitializeAsync();
        return await _cacheService.GetCacheMetadataAsync(podcastId);
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
            if (extension.OuterName == "image" && extension.OuterNamespace.Contains("itunes", StringComparison.Ordinal))
            {
                var reader = extension.GetReader();
                return reader.GetAttribute("href") ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string GetDuration(SyndicationItem item)
    {
        foreach (var extension in item.ElementExtensions)
        {
            if (extension.OuterName == "duration" && extension.OuterNamespace.Contains("itunes", StringComparison.Ordinal))
            {
                return extension.GetObject<string>();
            }
        }
        return string.Empty;
    }

    private static long GetFileSize(SyndicationItem item)
    {
        var enclosure = item.Links?.FirstOrDefault(l => l.RelationshipType == "enclosure");
        return enclosure?.Length ?? 0;
    }

    private static string ExtractEpisodeNumber(string title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;

        var span = title.AsSpan();
        var hashIndex = span.IndexOf('#');
        if (hashIndex < 0)
            return string.Empty;

        var afterHash = span[(hashIndex + 1)..];
        var spaceIndex = afterHash.IndexOf(' ');
        return (spaceIndex >= 0 ? afterHash[..spaceIndex] : afterHash).ToString();
    }

    public async Task<List<Episode>?> GetCachedEpisodesAsync(string podcastId)
    {
        try
        {
            await _cacheService.InitializeAsync();
            return await _cacheService.GetCachedEpisodesAsync(podcastId);
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
        GC.SuppressFinalize(this);
    }
}
