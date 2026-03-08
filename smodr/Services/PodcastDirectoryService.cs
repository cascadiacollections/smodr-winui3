using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace smodr.Services;

public sealed class PodcastDirectoryService
{
    private const string CacheFileName = "itunes_lookup.json";
    private const string LookupBaseUrl = "https://itunes.apple.com/lookup";
    private const int CacheExpiryDays = 7;

    private static readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Dictionary<long, PodcastLookupResult>? _cache;
    private StorageFolder? _cacheFolder;

    static PodcastDirectoryService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "smodr/1.0 (+https://github.com/cascadiacollections/smodr-winui3)");
    }

    private async Task EnsureInitializedAsync()
    {
        _cacheFolder ??= await ApplicationData.Current.LocalFolder
            .CreateFolderAsync("ImageCache", CreationCollisionOption.OpenIfExists);

        if (_cache is null)
        {
            _cache = await LoadCacheAsync() ?? [];
        }
    }

    /// <summary>
    /// Looks up multiple podcasts in a single iTunes API call and caches the results.
    /// </summary>
    public async Task<Dictionary<long, PodcastLookupResult>> LookupAsync(IEnumerable<long> applePodcastIds)
    {
        await EnsureInitializedAsync();

        var ids = applePodcastIds.ToList();
        var results = new Dictionary<long, PodcastLookupResult>();
        var idsToFetch = new List<long>();

        // Return cached entries that haven't expired
        foreach (var id in ids)
        {
            if (_cache!.TryGetValue(id, out var cached)
                && (DateTime.UtcNow - cached.RetrievedAt).TotalDays < CacheExpiryDays)
            {
                results[id] = cached;
            }
            else
            {
                idsToFetch.Add(id);
            }
        }

        if (idsToFetch.Count == 0)
        {
            Debug.WriteLine($"iTunes lookup: all {ids.Count} podcasts served from cache");
            return results;
        }

        try
        {
            var idsParam = string.Join(",", idsToFetch);
            var url = $"{LookupBaseUrl}?id={idsParam}";

            Debug.WriteLine($"iTunes lookup: fetching {idsToFetch.Count} podcast(s)");
            var json = await _httpClient.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<iTunesLookupResponse>(json, _jsonOptions);

            if (response?.Results is { } items)
            {
                foreach (var item in items)
                {
                    if (item.TrackId is 0 || string.IsNullOrEmpty(item.ArtworkUrl600))
                        continue;

                    var result = new PodcastLookupResult
                    {
                        TrackId = item.TrackId,
                        TrackName = item.TrackName ?? string.Empty,
                        ArtworkUrl600 = item.ArtworkUrl600,
                        RetrievedAt = DateTime.UtcNow
                    };

                    results[item.TrackId] = result;
                    _cache![item.TrackId] = result;
                }
            }

            await SaveCacheAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"iTunes lookup failed: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Gets the artwork URL for a single podcast, using the cache if available.
    /// </summary>
    public async Task<string?> GetArtworkUrlAsync(long applePodcastId)
    {
        var results = await LookupAsync([applePodcastId]);
        return results.TryGetValue(applePodcastId, out var result) ? result.ArtworkUrl600 : null;
    }

    private async Task<Dictionary<long, PodcastLookupResult>?> LoadCacheAsync()
    {
        try
        {
            if (_cacheFolder is null)
                return null;

            if (await _cacheFolder.TryGetItemAsync(CacheFileName) is not StorageFile file)
                return null;

            var json = await FileIO.ReadTextAsync(file);
            return JsonSerializer.Deserialize<Dictionary<long, PodcastLookupResult>>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading iTunes cache: {ex.Message}");
            return null;
        }
    }

    private async Task SaveCacheAsync()
    {
        try
        {
            if (_cacheFolder is null || _cache is null)
                return;

            var file = await _cacheFolder.CreateFileAsync(CacheFileName, CreationCollisionOption.ReplaceExisting);
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            await FileIO.WriteTextAsync(file, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving iTunes cache: {ex.Message}");
        }
    }
}

public sealed class PodcastLookupResult
{
    public long TrackId { get; init; }
    public string TrackName { get; init; } = string.Empty;
    public string ArtworkUrl600 { get; init; } = string.Empty;
    public DateTime RetrievedAt { get; init; }
}

file sealed class iTunesLookupResponse
{
    [JsonPropertyName("resultCount")]
    public int ResultCount { get; init; }

    [JsonPropertyName("results")]
    public List<iTunesLookupItem>? Results { get; init; }
}

file sealed class iTunesLookupItem
{
    [JsonPropertyName("trackId")]
    public long TrackId { get; init; }

    [JsonPropertyName("trackName")]
    public string? TrackName { get; init; }

    [JsonPropertyName("artworkUrl600")]
    public string? ArtworkUrl600 { get; init; }
}
