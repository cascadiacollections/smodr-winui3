namespace smodr.Models;

public record class Podcast
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string FeedUrl { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string Hosts { get; init; } = string.Empty;

    public static IReadOnlyList<Podcast> Catalog { get; } =
    [
        new()
        {
            Id = "smodcast",
            Name = "SModcast",
            Description = "The flagship podcast of the SModcast network.",
            FeedUrl = "https://feeds.feedburner.com/SModcasts",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/smodcast1400.jpg",
            Hosts = "Kevin Smith & Scott Mosier"
        },
        new()
        {
            Id = "babbleon",
            Name = "Hollywood Babble-On",
            Description = "Live show covering Hollywood news and pop culture.",
            FeedUrl = "https://feeds.feedburner.com/HollywoodBabbleOn",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/hollywoodbabbleon1400.jpg",
            Hosts = "Kevin Smith & Ralph Garman"
        },
        new()
        {
            Id = "jaysilentbob",
            Name = "Jay & Silent Bob Get Old",
            Description = "Podcast therapy keeping Jason Mewes on the straight and narrow.",
            FeedUrl = "https://feeds.feedburner.com/JayAndSilentBobGetOld",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/jsbgetold1400.jpg",
            Hosts = "Kevin Smith & Jason Mewes"
        },
        new()
        {
            Id = "fatmanbeyond",
            Name = "Fatman Beyond",
            Description = "Deep dives into superhero movies, TV, and geek culture.",
            FeedUrl = "https://feeds.feedburner.com/FatmanOnBatman",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/fatmanonbatman1400.jpg",
            Hosts = "Kevin Smith & Marc Bernardin"
        },
        new()
        {
            Id = "tesd",
            Name = "Tell 'Em Steve-Dave!",
            Description = "Three guys from New Jersey discuss life, work, and the absurd.",
            FeedUrl = "https://feeds.feedburner.com/TellEmSteveDave",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/tellemstevedave1400.jpg",
            Hosts = "Bryan Johnson, Walt Flanagan & Brian Quinn"
        },
        new()
        {
            Id = "smoviemakers",
            Name = "SMoviemakers",
            Description = "Conversations about the art and craft of filmmaking.",
            FeedUrl = "https://feeds.feedburner.com/Smoviemakers",
            ImageUrl = "https://smodcast.com/wp-content/blogs.dir/1/files_mf/smoviemakers1400.jpg",
            Hosts = "Kevin Smith"
        },
    ];
}
