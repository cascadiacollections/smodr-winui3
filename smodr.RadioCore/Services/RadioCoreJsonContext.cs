using System.Text.Json.Serialization;

namespace smodr.Services;

/// <summary>Source-generated metadata for the app preference files (default options, compact).</summary>
[JsonSerializable(typeof(RadioAppearancePreferences.AppearanceData))]
[JsonSerializable(typeof(RadioPlaybackPreferences.PreferencesData))]
[JsonSerializable(typeof(RadioPrivacySettings.PrivacyData))]
internal sealed partial class RadioCoreJsonContext : JsonSerializerContext;
