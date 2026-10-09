using smodr.Models;

namespace smodr.Services;

public sealed record RadioQuickLaunchItem(string Arguments, string Name, string Group);

/// <summary>Only bounded local station IDs enter shell arguments, never stream URLs or API keys.</summary>
public static class RadioQuickLaunch
{
    public static IReadOnlyList<RadioQuickLaunchItem> Build(IReadOnlyList<RadioStation> favorites,
        IReadOnlyList<RadioStation> recents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<RadioQuickLaunchItem>();
        foreach (var (stations, group) in new[] { (favorites, "Favorites"), (recents, "Recently played") })
        {
            foreach (var station in stations.Take(8))
            {
                if (!IsSafeId(station.Id) || string.IsNullOrWhiteSpace(station.Name)
                                          || station.Name.Length > 128 || station.Name.Any(char.IsControl) ||
                                          !seen.Add(station.Id))
                {
                    continue;
                }

                items.Add(new RadioQuickLaunchItem("--station-id=" + Uri.EscapeDataString(station.Id), station.Name,
                    group));
                if (items.Count == 8)
                {
                    return items;
                }
            }
        }

        return items;
    }

    public static bool TryParse(string arguments, out string id, string? executablePath = null)
    {
        id = string.Empty;
        const string prefix = "--station-id=";
        if (arguments.Length > 2048)
        {
            return false;
        }

        if (executablePath is { Length: > 0 })
        {
            var quotedPrefix = $"\"{executablePath}\" ";
            var plainPrefix = executablePath + " ";
            if (arguments.StartsWith(quotedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                arguments = arguments[quotedPrefix.Length..];
            }
            else if (arguments.StartsWith(plainPrefix, StringComparison.OrdinalIgnoreCase))
            {
                arguments = arguments[plainPrefix.Length..];
            }
        }

        if (arguments.Length > 1024 || !arguments.StartsWith(prefix, StringComparison.Ordinal)
                                    || arguments.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try { id = Uri.UnescapeDataString(arguments[prefix.Length..]); }
        catch (UriFormatException) { return false; }

        return IsSafeId(id);
    }

    private static bool IsSafeId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl);
    }
}
