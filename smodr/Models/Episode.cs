using System;

namespace smodr.Models
{
    public record class Episode
    {
        public required string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public required string MediaUrl { get; set; } = string.Empty;
        public DateTime PublishDate { get; set; }
        public string Duration { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int CurrentTime { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string EpisodeNumber { get; set; } = string.Empty;
        
        public string FormattedPublishDate => PublishDate.ToString("MMM dd, yyyy");
        public string FormattedDuration => Duration ?? "Unknown";
        public string FormattedFileSize => FileSize > 0 ? $"{FileSize / (1024 * 1024):F1} MB" : "Unknown";

        // Override equality to compare only by MediaUrl (business identity)
        public virtual bool Equals(Episode? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return MediaUrl == other.MediaUrl;
        }

        public override int GetHashCode()
        {
            return MediaUrl.GetHashCode();
        }
    }
}