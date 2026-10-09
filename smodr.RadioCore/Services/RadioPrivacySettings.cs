using System.Text.Json;

namespace smodr.Services;

/// <summary>Device-local privacy choices. Play reporting follows the iOS default.</summary>
public sealed class RadioPrivacySettings : IRadioPrivacySettings
{
    private readonly string _filePath;
    private readonly Lock _gate = new();
    private RadioPrivacyChoices _current;
    private RadioPrivacyChoices _persisted;
    private long _requestVersion;
    private Task _writeTail = Task.CompletedTask;

    public RadioPrivacySettings(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CascadiaCollections", "ShoutkitWindows", "privacy-settings.json");
        _current = _persisted = Read();
    }

    public RadioPrivacyChoices Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsPlayReportingEnabled => Current.PlayReportingEnabled;

    public bool IsAlbumArtworkEnabled => Current.AlbumArtworkEnabled;

    public Task SetPlayReportingEnabledAsync(bool enabled)
    {
        return Update(current => current with { PlayReportingEnabled = enabled });
    }

    public Task SetAlbumArtworkEnabledAsync(bool enabled)
    {
        return Update(current => current with { AlbumArtworkEnabled = enabled });
    }

    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _writeTail;
        }
    }

    private Task Update(Func<RadioPrivacyChoices, RadioPrivacyChoices> change)
    {
        lock (_gate)
        {
            var next = change(_current);
            if (next == _current)
            {
                return _writeTail;
            }

            _current = next;
            var operation = SaveAfterAsync(_writeTail, next, ++_requestVersion);
            _writeTail = ObserveCompletionAsync(operation);
            return operation;
        }
    }

    private RadioPrivacyChoices Read()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return RadioPrivacyChoices.Default;
            }

            // A damaged existing choice must not silently re-enable network reporting.
            var data = JsonSerializer.Deserialize<PrivacyData>(File.ReadAllText(_filePath));
            return data?.PlayReportingEnabled is { } reporting && data.SchemaVersion is null or 1
                ? new RadioPrivacyChoices(reporting,
                    data.AlbumArtworkEnabled ?? true) // Existing settings predate artwork.
                : RadioPrivacyChoices.FailClosed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Record("privacy.read", exception);
            return RadioPrivacyChoices.FailClosed;
        }
    }

    private async Task SaveAfterAsync(Task previous, RadioPrivacyChoices choices, long requestVersion)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await Task.Run(() => Save(choices)).ConfigureAwait(false);
            lock (_gate)
            {
                _persisted = choices;
            }
        }
        catch
        {
            lock (_gate)
            {
                // A later queued choice may still save successfully; only roll back
                // when this failure is still the latest requested snapshot.
                if (_requestVersion == requestVersion)
                {
                    _current = _persisted;
                }
            }

            throw;
        }
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch
        {
            /* The next setting change must still be writable. */
        }
    }

    private void Save(RadioPrivacyChoices choices)
    {
        AtomicFileWriter.WriteAllText(_filePath,
            JsonSerializer.Serialize(new PrivacyData
            {
                SchemaVersion = 1,
                PlayReportingEnabled = choices.PlayReportingEnabled,
                AlbumArtworkEnabled = choices.AlbumArtworkEnabled
            }));
    }

    private sealed class PrivacyData
    {
        public int? SchemaVersion { get; set; }
        public bool? PlayReportingEnabled { get; set; }
        public bool? AlbumArtworkEnabled { get; set; }
    }
}
