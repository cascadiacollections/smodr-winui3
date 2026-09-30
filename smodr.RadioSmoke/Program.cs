using smodr.Services;

if (args.Length is < 2 or > 3 || !Uri.TryCreate(args[0], UriKind.Absolute, out var streamUri)
    || streamUri.Scheme is not ("http" or "https")
    || !int.TryParse(args.Length == 3 ? args[2] : "3", out var samples)
    || samples is < 1 or > 10)
{
    await Console.Error.WriteLineAsync("Usage: smodr.RadioSmoke <http(s)-stream-url> <station-name> [samples: 1-10]");
    return 2;
}

var stationName = args[1];
using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
using var catalogClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
var probe = new IcyMetadataProbe(probeClient);
var artwork = new AlbumArtworkLookup(catalogClient);
var found = 0;
for (var index = 0; index < samples; index++)
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var result = await probe.ProbeAsync(streamUri, timeout.Token);
        if (!result.IsSupported)
        {
            await Console.Error.WriteLineAsync("FAIL: stream does not expose ICY metadata to the probe.");
            return 1;
        }
        var track = IcyTrackParser.Parse(result.RawMetadata, stationName);
        if (track is null)
        {
            await Console.Out.WriteLineAsync($"Sample {index + 1}: no accepted track (ad/preroll or empty cue).");
        }
        else
        {
            found++;
            var match = await artwork.FindAsync(track, timeout.Token);
            await Console.Out.WriteLineAsync($"Sample {index + 1}: {track.Display}; catalog art: {(match is null ? "none" : "plausible match")}");
        }
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
    {
        await Console.Error.WriteLineAsync($"Sample {index + 1}: {exception.GetType().Name}; check stream reachability.");
    }
    if (index < samples - 1) await Task.Delay(TimeSpan.FromSeconds(10));
}

await Console.Out.WriteLineAsync(found > 0 ? $"PASS: {found}/{samples} samples contained an accepted track."
    : "FAIL: no accepted track in the sample window.");
return found > 0 ? 0 : 1;
