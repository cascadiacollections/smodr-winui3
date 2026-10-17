using System.Text.Json;
using System.Text.Json.Serialization;

namespace smodr.Services;

/// <summary>
/// Source-generated metadata for the persisted library, history, directory snapshot and exports.
/// <see cref="Default" /> keeps the compact default-options format; <see cref="Indented" /> differs only by
/// <see cref="JsonSerializerOptions.WriteIndented" />. Both must stay byte-compatible with existing files.
/// </summary>
[JsonSerializable(typeof(RadioLibraryService.RadioLibraryData))]
[JsonSerializable(typeof(TrackHistoryService.HistoryData))]
[JsonSerializable(typeof(RadioDirectorySnapshotCache.CacheData))]
[JsonSerializable(typeof(LocalDataExport.HistoryExport))]
[JsonSerializable(typeof(LocalDataExport.DiagnosticsExport))]
internal sealed partial class RadioServicesJsonContext : JsonSerializerContext
{
    public static RadioServicesJsonContext Indented { get; } = new(new JsonSerializerOptions { WriteIndented = true });
}
