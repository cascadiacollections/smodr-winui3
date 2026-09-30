using System.Text.Json;

namespace smodr.Services;

/// <summary>Device-local privacy choices. Play reporting follows the iOS default.</summary>
public sealed class RadioPrivacySettings : IRadioPrivacySettings
{
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private Task _writeTail = Task.CompletedTask;
    private bool _playReportingEnabled;

    public RadioPrivacySettings(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections", "ShoutkitWindows", "privacy-settings.json");
        _playReportingEnabled = Read();
    }

    public bool IsPlayReportingEnabled
    {
        get { lock (_gate) return _playReportingEnabled; }
    }

    public Task SetPlayReportingEnabledAsync(bool enabled)
    {
        lock (_gate)
        {
            if (_playReportingEnabled == enabled) return _writeTail;
            _playReportingEnabled = enabled;
            var operation = SaveAfterAsync(_writeTail, enabled);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _writeTail;
    }

    private bool Read()
    {
        try
        {
            if (!File.Exists(_filePath)) return true;
            // A damaged existing choice must not silently re-enable network reporting.
            return JsonSerializer.Deserialize<PrivacyData>(File.ReadAllText(_filePath))?.PlayReportingEnabled ?? false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Record("privacy.read", exception);
            return false;
        }
    }

    private async Task SaveAfterAsync(Task previous, bool enabled)
    {
        await previous.ConfigureAwait(false);
        await Task.Run(() => Save(enabled)).ConfigureAwait(false);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* The next setting change must still be writable. */ }
    }

    private void Save(bool enabled)
    {
        AtomicFileWriter.WriteAllText(_filePath,
            JsonSerializer.Serialize(new PrivacyData { PlayReportingEnabled = enabled }));
    }

    private sealed class PrivacyData
    {
        public bool? PlayReportingEnabled { get; set; }
    }
}
