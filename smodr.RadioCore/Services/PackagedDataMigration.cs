namespace smodr.Services;

/// <summary>One-time, non-overwriting import from the unpackaged profile into package-local storage.</summary>
public static class PackagedDataMigration
{
    private static readonly (string Name, long MaxBytes)[] _files =
    [
        ("library.json", 2_000_000),
        ("track-history.json", 2_000_000),
        ("privacy-settings.json", 64_000),
        ("playback-settings.json", 64_000),
        ("directory-cache.json", 2_000_000)
    ];

    public static void Import(string legacyDirectory, string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        if (Path.GetFullPath(legacyDirectory).Equals(Path.GetFullPath(packageDirectory),
            StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(packageDirectory);
        foreach (var (name, maxBytes) in _files)
        {
            var source = Path.Combine(legacyDirectory, name);
            var destination = Path.Combine(packageDirectory, name);
            var temporary = Path.Combine(packageDirectory, $".{name}.{Guid.NewGuid():N}.tmp");
            try
            {
                var file = new FileInfo(source);
                if (!file.Exists || file.Length is <= 0 || file.Length > maxBytes
                    || File.Exists(destination)) continue;
                // Copy, then atomically publish. Never replace data already written by
                // a packaged launch, and never modify the original unpackaged file.
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                if (new FileInfo(temporary).Length > maxBytes) continue;
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException) { /* Existing destination or inaccessible source: leave both intact. */ }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
