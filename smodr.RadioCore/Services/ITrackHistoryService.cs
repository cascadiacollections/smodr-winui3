using smodr.Models;

namespace smodr.Services;

public interface ITrackHistoryService
{
    IReadOnlyList<HeardTrack> Entries { get; }
    Task RecordAsync(RadioStation station, RadioTrackInfo track);
    Task FlushAsync();
}
