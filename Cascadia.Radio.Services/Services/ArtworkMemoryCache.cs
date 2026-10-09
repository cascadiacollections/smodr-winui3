namespace smodr.Services;

/// <summary>Bounded LRU cache with monotonic expiry; native image caches remain scoped to their UI dispatcher.</summary>
public sealed class ArtworkMemoryCache<T>(
    int maxEntries,
    long maxWeight,
    TimeProvider? clock = null,
    RuntimeDiagnosticCounters? diagnostics = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly RuntimeDiagnosticCounters _diagnostics = diagnostics ?? RuntimeDiagnostics.Counters;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly Lock _gate = new();
    private readonly LinkedList<string> _order = new();
    private long _weight;

    public bool TryGet(string key, out T? value)
    {
        lock (_gate)
        {
            value = default;
            if (!_entries.TryGetValue(key, out var entry))
            {
                _diagnostics.Increment(RuntimeCounter.ArtworkMemoryMiss);
                return false;
            }

            if (_clock.GetElapsedTime(entry.CreatedAt) >= TimeSpan.FromMinutes(30))
            {
                Remove(key);
                _diagnostics.Increment(RuntimeCounter.ArtworkMemoryExpired);
                _diagnostics.Increment(RuntimeCounter.ArtworkMemoryMiss);
                return false;
            }

            _diagnostics.Increment(RuntimeCounter.ArtworkMemoryHit);
            _order.Remove(entry.Node);
            _order.AddLast(entry.Node);
            value = entry.Value;
            return true;
        }
    }

    public void Put(string key, T value, long weight)
    {
        if (weight <= 0 || weight > maxWeight || maxEntries <= 0)
        {
            return;
        }

        lock (_gate)
        {
            Remove(key);
            var node = _order.AddLast(key);
            _entries.Add(key, new Entry(value, weight, _clock.GetTimestamp(), node));
            _weight += weight;
            while ((_entries.Count > maxEntries || _weight > maxWeight) && _order.First is { } first)
            {
                Remove(first.Value);
            }
        }
    }

    private void Remove(string key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        _order.Remove(entry.Node);
        _weight -= entry.Weight;
    }

    private sealed record Entry(T Value, long Weight, long CreatedAt, LinkedListNode<string> Node);
}
