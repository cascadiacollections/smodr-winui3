using System.Text.Json;

namespace smodr.Services;

/// <summary>Allow-listed aggregate categories. Never add station, title, URL, or query labels.</summary>
public enum RuntimeCounter
{
    RecoveryRetryScheduled,
    RecoveryRestartRequested,
    RecoveryExhausted,
    MetadataAccepted,
    MetadataDuplicate,
    MetadataRejectedEmpty,
    MetadataRejectedOversize,
    MetadataRejectedDamaged,
    MetadataRejectedNonSong,
    MetadataRetired,
    ArtworkMemoryHit,
    ArtworkMemoryMiss,
    ArtworkMemoryExpired,
    ArtworkResponseRejected,
    ArtworkTransportCanceled,
    ArtworkTransportFailed,
    AlbumCacheHit,
    AlbumLookupStarted,
    AlbumLookupJoined,
    AlbumMatch,
    AlbumMiss,
    AlbumResponseRejected,
    AlbumTransportCanceled,
    AlbumTransportFailed
}

/// <summary>Constant-size process counters; hot paths do no IO and allocate no labels.</summary>
public sealed class RuntimeDiagnosticCounters
{
    private static readonly string[] _names = Enum.GetNames<RuntimeCounter>();
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly long[] _counts = new long[_names.Length];

    public void Increment(RuntimeCounter counter)
    {
        var index = (int)counter;
        if ((uint)index >= (uint)_counts.Length) throw new ArgumentOutOfRangeException(nameof(counter));
        Interlocked.Increment(ref _counts[index]);
    }

    /// <summary>A detached copy. Individual counts are atomic; this is not a transaction across categories.</summary>
    public Dictionary<string, long> Snapshot()
    {
        var result = new Dictionary<string, long>(_names.Length, StringComparer.Ordinal);
        for (var index = 0; index < _counts.Length; index++) result.Add(_names[index], Interlocked.Read(ref _counts[index]));
        return result;
    }

    public Task SaveAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => AtomicFileWriter.WriteAllText(filePath,
            JsonSerializer.Serialize(new DiagnosticSnapshot(1, DateTimeOffset.UtcNow, Snapshot()), _json)));
    }

    private sealed record DiagnosticSnapshot(int SchemaVersion, DateTimeOffset CapturedAtUtc, Dictionary<string, long> Counters);
}

public static class RuntimeDiagnostics
{
    public static RuntimeDiagnosticCounters Counters { get; } = new();
    public static Task FlushAsync(string filePath) => Counters.SaveAsync(filePath);
}
