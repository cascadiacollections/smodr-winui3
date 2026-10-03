using System.Text.Json;
using System.Text.Json.Serialization;

namespace smodr.Services;

[JsonConverter(typeof(JsonStringEnumConverter<RadioEqualizerPreset>))]
public enum RadioEqualizerPreset { Off, Speech, Bass, Treble }

public sealed record RadioPlaybackOptions(bool PrewarmStreams = false,
    bool LoopFinishedBroadcasts = false, RadioEqualizerPreset Equalizer = RadioEqualizerPreset.Off,
    bool JumpLists = false, bool ResumeAfterSleep = false, bool ResumeAfterNetworkLoss = true);

/// <summary>Serialized atomic writes publish only durable choices; OS resume policies have explicit defaults.</summary>
public sealed class RadioPlaybackPreferences
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private RadioPlaybackOptions _current = new();
    private Task _tail = Task.CompletedTask;
    public bool IsReadOnly { get; }

    public RadioPlaybackPreferences(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            using var input = File.OpenRead(path);
            var bytes = new byte[64_001];
            var count = 0;
            int read;
            while (count < bytes.Length && (read = input.Read(bytes.AsSpan(count))) > 0) count += read;
            if (count == bytes.Length) { IsReadOnly = true; return; }
            var data = JsonSerializer.Deserialize<PreferencesData>(bytes.AsSpan(0, count));
            if (data?.SchemaVersion != 1) { IsReadOnly = true; return; }
            if (data.Options is { } options && Enum.IsDefined(options.Equalizer)) _current = options;
            else IsReadOnly = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Record("playback.settings-read", exception);
            IsReadOnly = true;
        }
    }

    public RadioPlaybackOptions Current { get { lock (_gate) return _current; } }

    public Task UpdateAsync(Func<RadioPlaybackOptions, RadioPlaybackOptions> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            if (IsReadOnly) return Task.FromException(new InvalidOperationException("Playback settings are read-only. Preserve the existing file and use a compatible app version."));
            var operation = SaveAfterAsync(_tail, change);
            _tail = ObserveAsync(operation);
            return operation;
        }
    }

    public Task FlushAsync() { lock (_gate) return _tail; }

    private async Task SaveAfterAsync(Task previous, Func<RadioPlaybackOptions, RadioPlaybackOptions> change)
    {
        await previous.ConfigureAwait(false);
        RadioPlaybackOptions next;
        lock (_gate) next = change(_current);
        if (!Enum.IsDefined(next.Equalizer)) throw new ArgumentOutOfRangeException(nameof(change));
        if (next == Current) return;
        await Task.Run(() => AtomicFileWriter.WriteAllText(_path,
            JsonSerializer.Serialize(new PreferencesData(1, next)))).ConfigureAwait(false);
        lock (_gate) _current = next;
    }

    private static async Task ObserveAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* The initiating edit observes the failure; later writes can still run. */ }
    }

    private sealed record PreferencesData(int SchemaVersion, RadioPlaybackOptions? Options);
}

public readonly record struct RadioEqualizerBand(double Frequency, double Gain);

public static class RadioEqualizerProfiles
{
    public static IReadOnlyList<RadioEqualizerBand> Bands(RadioEqualizerPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        double[] db = preset switch
        {
            RadioEqualizerPreset.Speech => [-2, 3, 3, -2],
            RadioEqualizerPreset.Bass => [4, 0, 0, -1],
            RadioEqualizerPreset.Treble => [-1, 0, 1, 3],
            _ => [0, 0, 0, 0],
        };
        double[] frequencies = [100, 800, 2000, 8000];
        return frequencies.Select((frequency, index) => new RadioEqualizerBand(frequency, Math.Pow(10, db[index] / 20))).ToArray();
    }

    public static double Headroom(RadioEqualizerPreset preset) =>
        1 / Math.Max(1, Bands(preset).Max(band => band.Gain));
}

public static class FinishedBroadcastPolicy
{
    public static bool ShouldLoop(bool enabled, bool requested, TimeSpan duration) =>
        enabled && requested && duration >= TimeSpan.FromSeconds(1) && duration < TimeSpan.MaxValue;
}
