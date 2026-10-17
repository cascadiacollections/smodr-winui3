using System.Text.Json;
using System.Text.Json.Serialization;
using smodr.Models;

namespace smodr.Services;

/// <summary>User-requested local snapshots; no transport or profile mutation.</summary>
public static class LocalDataExport
{
    public static async Task<string> HistoryAsync(ITrackHistoryService history)
    {
        ArgumentNullException.ThrowIfNull(history);
        await history.FlushAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(new HistoryExport(1, DateTimeOffset.UtcNow, history.Entries),
            RadioServicesJsonContext.Indented.HistoryExport);
    }

    public static string Diagnostics(RuntimeDiagnosticCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        return JsonSerializer.Serialize(new DiagnosticsExport(1, DateTimeOffset.UtcNow, counters.Snapshot()),
            RadioServicesJsonContext.Indented.DiagnosticsExport);
    }

    // Exported names were camelCase anonymous members; nested models keep their own names.
    internal sealed record HistoryExport(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("exportedAtUtc")] DateTimeOffset ExportedAtUtc,
        [property: JsonPropertyName("entries")] IReadOnlyList<HeardTrack> Entries);

    internal sealed record DiagnosticsExport(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("capturedAtUtc")] DateTimeOffset CapturedAtUtc,
        [property: JsonPropertyName("counters")] Dictionary<string, long> Counters);
}
