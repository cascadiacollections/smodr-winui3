using System.Text.Json;
using System.Text.Json.Serialization;

namespace smodr.Services;

/// <summary>Source-generated metadata for files written by Cascadia.Radio (web casing, indented).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(RuntimeDiagnosticCounters.DiagnosticSnapshot))]
internal sealed partial class RadioJsonContext : JsonSerializerContext;
