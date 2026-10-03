namespace smodr.Services;

public interface IContinuousTrackMetadataReader
{
    /// <summary>Returns false when the response does not advertise ICY metadata.</summary>
    Task<bool> ListenAsync(Uri streamUri, Action<string> onMetadata,
        CancellationToken cancellationToken = default);
}
