using smodr.Models;

namespace smodr.Services;

public interface IRadioDirectorySnapshotCache
{
    Task<IReadOnlyList<RadioStation>?> GetAsync(string key, TimeSpan maxAge);
    Task StoreAsync(string key, IReadOnlyList<RadioStation> stations);
    Task FlushAsync();
}
