using smodr.Models;

namespace smodr.Services;

public interface IAlbumArtworkLookup
{
    Task<AlbumArtworkMatch?> FindAsync(RadioTrackInfo track, CancellationToken cancellationToken = default);
}
