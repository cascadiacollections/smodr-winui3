using smodr.Models;

namespace smodr.Services;

public enum TopTracksTimeframe { Week, Month, AllTime }

/// <summary>Derives a ranking from retained heard-track rows; no second store is needed.</summary>
public static class TopTracksAggregator
{
    public static IReadOnlyList<TopTrack> Aggregate(IEnumerable<HeardTrack> history,
        TopTracksTimeframe timeframe, DateTimeOffset now, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var since = timeframe switch
        {
            TopTracksTimeframe.Week => now.AddDays(-7),
            TopTracksTimeframe.Month => now.AddMonths(-1),
            TopTracksTimeframe.AllTime => DateTimeOffset.MinValue,
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe))
        };
        var buckets = new Dictionary<(string Title, string Artist), TopTrack>();
        foreach (var row in history)
        {
            if (string.IsNullOrWhiteSpace(row.Title) || string.IsNullOrWhiteSpace(row.Artist)
                                                     || row.HeardAt < since)
            {
                continue;
            }

            var key = (row.Title.ToUpperInvariant(), row.Artist.ToUpperInvariant());
            buckets[key] = buckets.TryGetValue(key, out var existing)
                ? new TopTrack(
                    row.HeardAt > existing.LastHeardAt ? row.Title : existing.Title,
                    row.HeardAt > existing.LastHeardAt ? row.Artist : existing.Artist,
                    existing.PlayCount + 1,
                    row.HeardAt > existing.LastHeardAt ? row.HeardAt : existing.LastHeardAt)
                : new TopTrack(row.Title, row.Artist, 1, row.HeardAt);
        }

        return
        [
            .. buckets.Values.OrderByDescending(track => track.PlayCount)
                .ThenByDescending(track => track.LastHeardAt).Take(limit)
        ];
    }
}
