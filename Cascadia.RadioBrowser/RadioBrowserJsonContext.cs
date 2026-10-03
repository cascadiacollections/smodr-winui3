using System.Text.Json.Serialization;

namespace Cascadia.RadioBrowser;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(Station))]
[JsonSerializable(typeof(DirectoryValue[]))]
internal sealed partial class RadioBrowserJsonContext : JsonSerializerContext;
