using System.Security.Cryptography;
using System.Text;

namespace smodr.Services;

/// <summary>Best-effort cache of already validated station artwork bytes.</summary>
public sealed class StationArtworkDiskCache(string? directory = null, TimeProvider? clock = null)
{
    private const int MaxArtworkBytes = 1_000_000;
    private const int MaxFiles = 48;
    private readonly string _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CascadiaCollections", "ShoutkitWindows", "station-artwork");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<byte[]?> TryReadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var path = PathFor(uri);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaxArtworkBytes
                || _clock.GetUtcNow() - file.LastWriteTimeUtc > TimeSpan.FromDays(30))
            {
                return null;
            }

            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task StoreAsync(Uri uri, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is <= 0 or > MaxArtworkBytes) return;
        try
        {
            Directory.CreateDirectory(_directory);
            var temporaryPath = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, PathFor(uri), true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            await Task.Run(Prune, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Artwork is optional. A read-only profile must not hide the station.
        }
    }

    private void Prune()
    {
        var files = new DirectoryInfo(_directory).GetFiles("*.img")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxFiles);
        foreach (var file in files)
        {
            try { file.Delete(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private string PathFor(Uri uri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(_directory, $"{hash}.img");
    }
}
