namespace smodr.Services;

public static class LocalDiagnosticLog
{
    private const long MaxLogBytes = 1_000_000;
    private static readonly Lock _gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CascadiaCollections", "ShoutkitWindows", "diagnostics.log");


    public static void Record(string operation, Exception exception)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    File.Move(LogPath, $"{LogPath}.previous", true);
                }

                // Do not persist station URLs or HTTP response bodies in local diagnostics.
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.UtcNow:O} {operation} {exception.GetType().FullName} 0x{exception.HResult:X8}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never prevent launching or playing a station.
        }
    }
}
