using smodr.Models;

namespace smodr.Services;

public interface ITrackHistoryService
{
    IReadOnlyList<HeardTrack> Entries { get; }
    Task<Guid> RecordAsync(RadioStation station, RadioTrackInfo track);
    Task UpdateArtworkAsync(Guid entryId, AlbumArtworkMatch artwork);
    Task FlushAsync();
}
