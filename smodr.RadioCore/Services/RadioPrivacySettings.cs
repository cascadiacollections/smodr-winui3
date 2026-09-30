using System.Text.Json;

namespace smodr.Services;

/// <summary>Device-local privacy choices. Play reporting follows the iOS default.</summary>
public sealed class RadioPrivacySettings : IRadioPrivacySettings
{
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private Task _writeTail = Task.CompletedTask;
    private bool _playReportingEnabled;
    private bool _albumArtworkEnabled;

    public RadioPrivacySettings(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections", "ShoutkitWindows", "privacy-settings.json");
        (_playReportingEnabled, _albumArtworkEnabled) = Read();
    }

    public bool IsPlayReportingEnabled
    {
        get { lock (_gate) return _playReportingEnabled; }
    }

    public bool IsAlbumArtworkEnabled
    {
        get { lock (_gate) return _albumArtworkEnabled; }
    }

    public Task SetPlayReportingEnabledAsync(bool enabled)
    {
        lock (_gate)
        {
            if (_playReportingEnabled == enabled) return _writeTail;
            _playReportingEnabled = enabled;
            var operation = SaveAfterAsync(_writeTail, _playReportingEnabled, _albumArtworkEnabled);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task SetAlbumArtworkEnabledAsync(bool enabled)
    {
        lock (_gate)
        {
            if (_albumArtworkEnabled == enabled) return _writeTail;
            _albumArtworkEnabled = enabled;
            var operation = SaveAfterAsync(_writeTail, _playReportingEnabled, _albumArtworkEnabled);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _writeTail;
    }

    private (bool PlayReporting, bool AlbumArtwork) Read()
    {
        try
        {
            if (!File.Exists(_filePath)) return (true, true);
            // A damaged existing choice must not silently re-enable network reporting.
            var data = JsonSerializer.Deserialize<PrivacyData>(File.ReadAllText(_filePath));
            return data?.PlayReportingEnabled is { } reporting
                ? (reporting, data.AlbumArtworkEnabled ?? true) // Existing settings predate artwork.
                : (false, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Record("privacy.read", exception);
            return (false, false);
        }
    }

    private async Task SaveAfterAsync(Task previous, bool playReporting, bool albumArtwork)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() => Save(playReporting, albumArtwork)).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* The next setting change must still be writable. */ }
    }

    private void Save(bool playReporting, bool albumArtwork)
    {
        AtomicFileWriter.WriteAllText(_filePath,
            JsonSerializer.Serialize(new PrivacyData
            {
                PlayReportingEnabled = playReporting,
                AlbumArtworkEnabled = albumArtwork
            }));
    }

    private sealed class PrivacyData
    {
        public bool? PlayReportingEnabled { get; set; }
        public bool? AlbumArtworkEnabled { get; set; }
    }
}
