using System.Text.Json.Serialization;

namespace Cascadia.RadioBrowser;

/// <summary>Wire data only; formatting and presentation belong to the consuming application.</summary>
public sealed record Station
{
    [JsonPropertyName("stationuuid")] public string StationUuid { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("url_resolved")] public string ResolvedUrl { get; init; } = "";
    [JsonPropertyName("homepage")] public string Homepage { get; init; } = "";
    [JsonPropertyName("favicon")] public string Favicon { get; init; } = "";
    [JsonPropertyName("tags")] public string Tags { get; init; } = "";
    [JsonPropertyName("countrycode")] public string CountryCode { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("language")] public string Language { get; init; } = "";
    [JsonPropertyName("codec")] public string Codec { get; init; } = "";
    [JsonPropertyName("bitrate")] public int Bitrate { get; init; }
    [JsonPropertyName("votes")] public int Votes { get; init; }
    [JsonPropertyName("clickcount")] public int ClickCount { get; init; }
    [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; init; }
}

public sealed record DirectoryValue
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("stationcount")] public int StationCount { get; init; }
}

public enum StationOrder { Name, ClickCount, Votes, Bitrate, Random }

public enum StationRanking { Popular, Votes, RecentlyClicked, RecentlyChanged }

public enum DirectoryFacet { CountryCodes, Languages, Tags, Codecs }

public enum ClientDiagnostic { MirrorFailed, DiscoveryFailed, ReportRejected }

/// <summary>No URLs, queries, station names, titles or response bodies are emitted.</summary>
public interface IClientDiagnostics
{
    void Record(ClientDiagnostic diagnostic);
}

public interface IMirrorProvider
{
    Task<IReadOnlyList<Uri>> GetServersAsync(CancellationToken cancellationToken = default);
}

public sealed record SearchOptions
{
    public string? Name { get; init; }
    public string? CountryCode { get; init; }
    public string? Tag { get; init; }
    public string? Language { get; init; }
    public string? Codec { get; init; }
    public int? BitrateMin { get; init; }
    public int? BitrateMax { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
    public StationOrder Order { get; init; } = StationOrder.ClickCount;
    public bool Reverse { get; init; } = true;
    public bool HideBroken { get; init; } = true;
}

public sealed record ClientOptions
{
    /// <summary>Identify your consuming application, not just this library.</summary>
    public required string UserAgent { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed class DirectoryUnavailableException() : Exception("Radio Browser directory unavailable.");
