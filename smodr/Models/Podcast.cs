namespace smodr.Models;

public record class Podcast
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string FeedUrl { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string Hosts { get; init; } = string.Empty;
    public long? ApplePodcastId { get; init; }

    public virtual bool Equals(Podcast? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public static IReadOnlyList<Podcast> Catalog { get; } =
    [
        new()
        {
            Id = "smodcast",
            Name = "SModcast",
            Description = "The flagship podcast of the SModcast network.",
            FeedUrl = "https://feeds.feedburner.com/SModcasts",
            ImageUrl = "https://i1.sndcdn.com/avatars-000375494753-odz2eo-original.jpg",
            Hosts = "Kevin Smith & Scott Mosier",
            ApplePodcastId = 215010467
        },
        new()
        {
            Id = "babbleon",
            Name = "Hollywood Babble-On",
            Description = "Live show covering Hollywood news and pop culture.",
            FeedUrl = "https://feeds.feedburner.com/HollywoodBabbleOn",
            ImageUrl = "https://is1-ssl.mzstatic.com/image/thumb/Podcasts123/v4/62/88/07/62880736-f815-844d-1368-0d8aa135b897/mza_6363975001724393925.png/600x600bb.jpg",
            Hosts = "Kevin Smith & Ralph Garman",
            ApplePodcastId = 389193138
        },
        new()
        {
            Id = "jaysilentbob",
            Name = "Jay & Silent Bob Get Old",
            Description = "Podcast therapy keeping Jason Mewes on the straight and narrow.",
            FeedUrl = "https://feeds.feedburner.com/JayAndSilentBobGetOld",
            ImageUrl = "https://i1.sndcdn.com/avatars-000309983285-92en5d-original.jpg",
            Hosts = "Kevin Smith & Jason Mewes",
            ApplePodcastId = 388545763
        },
        new()
        {
            Id = "fatmanbeyond",
            Name = "Fatman Beyond",
            Description = "Deep dives into superhero movies, TV, and geek culture.",
            FeedUrl = "https://feeds.feedburner.com/FatmanOnBatman",
            ImageUrl = "https://i1.sndcdn.com/avatars-000607513431-dg9ub7-original.jpg",
            Hosts = "Kevin Smith & Marc Bernardin",
            ApplePodcastId = 532661418
        },
        new()
        {
            Id = "tesd",
            Name = "Tell 'Em Steve-Dave!",
            Description = "Three guys from New Jersey discuss life, work, and the absurd.",
            FeedUrl = "https://feeds.feedburner.com/TellEmSteveDave",
            ImageUrl = "https://megaphone.imgix.net/podcasts/38dc55da-5cb9-11ee-b82f-cf4026c6a8c7/image/1478271567245-SP7E52DRBWKN5JSW9GLA.jpeg?ixlib=rails-4.3.1&max-w=3000&max-h=3000&fit=crop&auto=format,compress",
            Hosts = "Bryan Johnson, Walt Flanagan & Brian Quinn",
            ApplePodcastId = 357537542
        },
        new()
        {
            Id = "smoviemakers",
            Name = "SMoviemakers",
            Description = "Conversations about the art and craft of filmmaking.",
            FeedUrl = "https://feeds.feedburner.com/Smoviemakers",
            ImageUrl = "https://i1.sndcdn.com/avatars-000099184010-sffeux-original.jpg",
            Hosts = "Kevin Smith"
        },
    ];
}
