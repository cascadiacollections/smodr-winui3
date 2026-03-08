namespace smodr.Models;

public record class Episode
{
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string MediaUrl { get; init; }
    public DateTime PublishDate { get; init; }
    public string Duration { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public int CurrentTime { get; set; }
    public string ImageUrl { get; init; } = string.Empty;
    public string EpisodeNumber { get; init; } = string.Empty;

    public string FormattedPublishDate => PublishDate.ToString("MMM dd, yyyy", System.Globalization.CultureInfo.CurrentCulture);
    public string FormattedDuration => string.IsNullOrEmpty(Duration) ? "Unknown" : Duration;
    public string FormattedFileSize => FileSize > 0 ? $"{FileSize / (1024 * 1024):F1} MB" : "Unknown";
    public string MetadataLine => $"{FormattedPublishDate} · {FormattedDuration}";

    public virtual bool Equals(Episode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(MediaUrl, other.MediaUrl, StringComparison.Ordinal);
    }

    public override int GetHashCode() => MediaUrl.GetHashCode(StringComparison.Ordinal);
}
