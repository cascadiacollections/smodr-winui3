namespace smodr.Services;

/// <summary>Host-configured compatibility diagnostics. Silent by default; never owns disk IO.</summary>
public static class AppDiagnostics
{
    private static Action<string, Exception>? _sink;

    public static void Configure(Action<string, Exception>? sink)
    {
        Volatile.Write(ref _sink, sink);
    }

    public static void Record(string operation, Exception exception)
    {
        try { Volatile.Read(ref _sink)?.Invoke(operation, exception); }
        catch (Exception)
        {
            /* Observability cannot interrupt radio work. */
        }
    }
}
