using System;
using System.Collections.Generic;
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
        private const string SMODCAST_RSS_URL = "https://feeds.feedburner.com/SModcasts";

        public DataService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<List<Episode>> GetEpisodesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(SMODCAST_RSS_URL);
                response.EnsureSuccessStatusCode();
                
                var rssContent = await response.Content.ReadAsStringAsync();
                
                using var xmlReader = XmlReader.Create(new System.IO.StringReader(rssContent));
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching episodes: {ex.Message}");
                return new List<Episode>();
            }
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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}