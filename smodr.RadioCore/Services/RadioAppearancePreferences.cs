using System.Text.Json;
using System.Text.Json.Serialization;

namespace smodr.Services;

[JsonConverter(typeof(JsonStringEnumConverter<RadioWindowBackdrop>))]
public enum RadioWindowBackdrop { Acrylic, Mica, Solid }

/// <summary>Small, versioned appearance choice. Unknown/corrupt data is preserved with a solid fallback.</summary>
public sealed class RadioAppearancePreferences
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private RadioWindowBackdrop _current = RadioWindowBackdrop.Acrylic;
    public bool IsReadOnly { get; }

    public RadioAppearancePreferences(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            using var stream = File.OpenRead(path);
            if (stream.Length > 64_000) throw new InvalidDataException();
            var bytes = new byte[64_001];
            var count = 0;
            int read;
            while (count < bytes.Length && (read = stream.Read(bytes.AsSpan(count))) > 0) count += read;
            if (count == bytes.Length) throw new InvalidDataException();
            var data = JsonSerializer.Deserialize<AppearanceData>(bytes.AsSpan(0, count));
            if (data?.SchemaVersion == 1 && data.Backdrop is { } value && Enum.IsDefined(value)) { _current = value; return; }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Record("appearance.read", exception);
        }
        _current = RadioWindowBackdrop.Solid;
        IsReadOnly = true;
    }

    public RadioWindowBackdrop Current { get { lock (_gate) return _current; } }

    public Task SetAsync(RadioWindowBackdrop backdrop)
    {
        if (!Enum.IsDefined(backdrop)) throw new ArgumentOutOfRangeException(nameof(backdrop));
        lock (_gate)
        {
            if (IsReadOnly) return Task.FromException(new InvalidOperationException("Appearance settings are read-only."));
            var operation = SaveAfterAsync(_tail, backdrop);
            _tail = ObserveAsync(operation);
            return operation;
        }
    }

    public Task FlushAsync() { lock (_gate) return _tail; }

    private async Task SaveAfterAsync(Task previous, RadioWindowBackdrop backdrop)
    {
        await previous.ConfigureAwait(false);
        if (backdrop == Current) return;
        await Task.Run(() => AtomicFileWriter.WriteAllText(_path,
            JsonSerializer.Serialize(new AppearanceData(1, backdrop)))).ConfigureAwait(false);
        lock (_gate) _current = backdrop;
    }

    private static async Task ObserveAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch { /* The initiating editor observes the error; subsequent saves remain usable. */ }
    }

    private sealed record AppearanceData(int SchemaVersion, RadioWindowBackdrop? Backdrop);
}
