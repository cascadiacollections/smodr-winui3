using smodr.Models;
using Windows.UI.StartScreen;

namespace smodr.Services;

internal sealed partial class RadioJumpList(RadioPlaybackPreferences preferences) : IDisposable
{
    private readonly Lock _gate = new();
#pragma warning disable IDE0028 // Preserve case-sensitive shell arguments.
    private readonly HashSet<string> _removedArguments = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    private Task _tail = Task.CompletedTask;
    private long _version;
    private bool _disposed;

    public Task UpdateAsync(IReadOnlyList<RadioStation> favorites, IReadOnlyList<RadioStation> recents)
    {
        var desired = preferences.Current.JumpLists ? RadioQuickLaunch.Build(favorites, recents)
            : Array.Empty<RadioQuickLaunchItem>();
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            _tail = UpdateAfterAsync(_tail, Interlocked.Increment(ref _version), desired);
            return _tail;
        }
    }

    private async Task UpdateAfterAsync(Task previous, long version, IReadOnlyList<RadioQuickLaunchItem> desired)
    {
        await previous;
        try
        {
            if (!IsSupported || _disposed) return;
            if (version != Volatile.Read(ref _version)) return;
            var list = await JumpList.LoadCurrentAsync();
            if (version != Volatile.Read(ref _version)) return;
            foreach (var item in list.Items.Where(item => item.RemovedByUser)) _removedArguments.Add(item.Arguments);
            list.Items.Clear();
            list.SystemGroupKind = JumpListSystemGroupKind.None;
            foreach (var item in desired.Where(item => !_removedArguments.Contains(item.Arguments)))
            {
                var entry = JumpListItem.CreateWithArguments(item.Arguments, item.Name);
                entry.GroupName = item.Group;
                list.Items.Add(entry);
            }
            await list.SaveAsync();
        }
        catch (Exception exception) { AppDiagnostics.Record("shell.jump-list", exception); }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; Interlocked.Increment(ref _version); }
        GC.SuppressFinalize(this);
    }

    internal static bool IsSupported
    {
        get
        {
            try { return JumpList.IsSupported(); }
            catch (Exception exception) { AppDiagnostics.Record("shell.jump-list-support", exception); return false; }
        }
    }

    internal Task FlushAsync() { lock (_gate) return _tail; }
}
