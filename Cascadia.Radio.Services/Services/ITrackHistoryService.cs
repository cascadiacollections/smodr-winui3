using smodr.Models;

namespace smodr.Services;

public interface ITrackHistoryService
{
    IReadOnlyList<HeardTrack> Entries { get; }
    Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track);
    Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork);
    /// <summary>Removes every local heard-track entry. Pending artwork updates for removed entries become no-ops.</summary>
    Task ClearAsync();
    Task FlushAsync();
}
