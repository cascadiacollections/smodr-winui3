using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class PackagedDataMigrationTests
{
    [TestMethod]
    public async Task CopiesKnownFilesOnceWithoutOverwritingEitherProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shoutkit-import-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "legacy");
        var packaged = Path.Combine(root, "packaged");
        try
        {
            Directory.CreateDirectory(legacy);
            await File.WriteAllTextAsync(Path.Combine(legacy, "library.json"), "legacy-library");
            await File.WriteAllTextAsync(Path.Combine(legacy, "privacy-settings.json"), "legacy-privacy");
            await File.WriteAllTextAsync(Path.Combine(legacy, "playback-settings.json"), "legacy-playback");
            await File.WriteAllTextAsync(Path.Combine(legacy, "appearance-settings.json"), "legacy-appearance");
            await File.WriteAllTextAsync(Path.Combine(legacy, "unexpected.json"), "not imported");

            PackagedDataMigration.Import(legacy, packaged);
            Assert.AreEqual("legacy-library", await File.ReadAllTextAsync(Path.Combine(packaged, "library.json")));
            Assert.AreEqual("legacy-playback", await File.ReadAllTextAsync(Path.Combine(packaged, "playback-settings.json")));
            Assert.AreEqual("legacy-appearance", await File.ReadAllTextAsync(Path.Combine(packaged, "appearance-settings.json")));
            Assert.IsFalse(File.Exists(Path.Combine(packaged, "unexpected.json")));
            await File.WriteAllTextAsync(Path.Combine(packaged, "library.json"), "packaged-library");
            PackagedDataMigration.Import(legacy, packaged);
            Assert.AreEqual("packaged-library", await File.ReadAllTextAsync(Path.Combine(packaged, "library.json")));
            Assert.AreEqual("legacy-library", await File.ReadAllTextAsync(Path.Combine(legacy, "library.json")));
            Assert.AreEqual("legacy-privacy", await File.ReadAllTextAsync(
                Path.Combine(packaged, "privacy-settings.json")));
        }
        finally { Cleanup(root); }
    }

    [TestMethod]
    public async Task OversizedFileIsNotImported()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shoutkit-import-{Guid.NewGuid():N}");
        var legacy = Path.Combine(root, "legacy");
        var packaged = Path.Combine(root, "packaged");
        try
        {
            Directory.CreateDirectory(legacy);
            await File.WriteAllBytesAsync(Path.Combine(legacy, "privacy-settings.json"), new byte[64_001]);
            PackagedDataMigration.Import(legacy, packaged);
            Assert.IsFalse(File.Exists(Path.Combine(packaged, "privacy-settings.json")));
        }
        finally { Cleanup(root); }
    }

    private static void Cleanup(string root)
    {
        var resolved = Path.GetFullPath(root);
        if (!Path.GetDirectoryName(resolved)!.Equals(Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("shoutkit-import-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected migration test directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
