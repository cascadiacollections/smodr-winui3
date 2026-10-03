namespace smodr.Models;

public sealed record RadioTrackInfo(string Title, string? Artist)
{
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";
}
