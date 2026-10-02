namespace smodr.Services;

/// <summary>Single-owner, short-lived prepared source. A take transfers ownership exactly once.</summary>
public sealed class RadioStreamWarmupSlot<T>(TimeProvider? clock = null) : IDisposable where T : class, IDisposable
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private T? _resource;
    private string? _url;
    private DateTimeOffset _createdAt;
    private bool _disposed;

    public void Put(string url, T resource)
    {
        T? retired;
        lock (_gate)
        {
            retired = _disposed ? resource : _resource;
            if (!_disposed) { _resource = resource; _url = url; _createdAt = _clock.GetUtcNow(); }
        }
        retired?.Dispose();
    }

    public T? Take(string url, bool allowed)
    {
        T? resource;
        bool usable;
        lock (_gate)
        {
            resource = _resource;
            usable = !_disposed && allowed && string.Equals(url, _url, StringComparison.Ordinal)
                && _clock.GetUtcNow() - _createdAt < TimeSpan.FromSeconds(30);
            _resource = null;
            _url = null;
        }
        if (usable) return resource;
        resource?.Dispose();
        return null;
    }

    public void Clear()
    {
        T? retired;
        lock (_gate) { retired = _resource; _resource = null; _url = null; }
        retired?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Clear();
        GC.SuppressFinalize(this);
    }
}
