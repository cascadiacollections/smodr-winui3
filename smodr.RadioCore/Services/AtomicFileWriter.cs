namespace smodr.Services;

/// <summary>Atomic same-directory replacement with a short Windows sharing-lock retry.</summary>
internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            ReplaceWithRetry(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static void ReplaceWithRetry(string sourcePath, string destinationPath)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, true);
                return;
            }
            catch (Exception exception) when (attempt < 3
                && !Directory.Exists(destinationPath)
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(25 << attempt);
            }
        }
    }
}
