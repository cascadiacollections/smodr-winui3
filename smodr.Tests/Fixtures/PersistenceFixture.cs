namespace smodr.Tests.Fixtures;

/// <summary>
/// Checked-in persisted files captured from the shipped (reflection-based) serializer.
/// Writers use the platform newline, so fixtures are stored with LF and normalized on read.
/// </summary>
internal sealed class PersistenceFixture : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"shoutkit-persistence-{Guid.NewGuid():N}");

    public static string Read(string name)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Persistence", name));
        return text.TrimEnd('\r', '\n').ReplaceLineEndings(Environment.NewLine);
    }

    /// <summary>Copies a fixture to a private temp path and returns that path.</summary>
    public string Install(string name, string fileName = "data.json")
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, Read(name));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
