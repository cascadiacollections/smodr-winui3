namespace smodr.Services;

public static class AppDiagnostics
{
    private const long MaxLogBytes = 1_000_000;
    private static readonly Lock _gate = new();
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CascadiaCollections", "ShoutkitWindows", "diagnostics.log");

    public static string LogPath => _logPath;

    public static void Record(string operation, Exception exception)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(_logPath)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxLogBytes)
                {
                    File.Move(_logPath, $"{_logPath}.previous", true);
                }

                // Do not persist station URLs or HTTP response bodies in local diagnostics.
                File.AppendAllText(_logPath,
                    $"{DateTimeOffset.UtcNow:O} {operation} {exception.GetType().FullName} 0x{exception.HResult:X8}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never prevent launching or playing a station.
        }
    }
}
