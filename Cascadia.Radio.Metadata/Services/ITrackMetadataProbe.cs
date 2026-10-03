namespace smodr.Services;

public readonly record struct IcyProbeResult(bool IsSupported, string? RawMetadata)
{
    public static IcyProbeResult Unsupported => new(false, null);
}

public interface ITrackMetadataProbe
{
    Task<IcyProbeResult> ProbeAsync(Uri streamUri, CancellationToken cancellationToken = default);
}
