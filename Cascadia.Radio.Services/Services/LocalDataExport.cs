using System.Text.Json;

namespace smodr.Services;

/// <summary>User-requested local snapshots; no transport or profile mutation.</summary>
public static class LocalDataExport
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public static async Task<string> HistoryAsync(ITrackHistoryService history)
    {
        ArgumentNullException.ThrowIfNull(history);
        await history.FlushAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            exportedAtUtc = DateTimeOffset.UtcNow,
            entries = history.Entries
        }, _json);
    }

    public static string Diagnostics(RuntimeDiagnosticCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTimeOffset.UtcNow,
            counters = counters.Snapshot()
        }, _json);
    }
}
