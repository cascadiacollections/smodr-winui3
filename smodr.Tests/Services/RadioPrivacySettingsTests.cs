using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioPrivacySettingsTests
{
    [TestMethod]
    public async Task DefaultsOnAndPersistsOffAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new RadioPrivacySettings(file);
            Assert.IsTrue(settings.IsPlayReportingEnabled);
            Assert.IsTrue(settings.IsAlbumArtworkEnabled);
            await settings.SetPlayReportingEnabledAsync(false);
            await settings.SetAlbumArtworkEnabledAsync(false);
            await settings.FlushAsync();
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
            Assert.IsFalse(new RadioPrivacySettings(file).IsAlbumArtworkEnabled);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ConcurrentChangesPersistLastChoice()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new RadioPrivacySettings(file);
            var first = settings.SetPlayReportingEnabledAsync(false);
            var second = settings.SetPlayReportingEnabledAsync(true);
            var third = settings.SetPlayReportingEnabledAsync(false);
            await Task.WhenAll(first, second, third);
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExistingSettingsEnableArtworkAndConcurrentChoicesDoNotClobberEachOther()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(file, """{"PlayReportingEnabled":false}""");
            var settings = new RadioPrivacySettings(file);
            Assert.IsFalse(settings.IsPlayReportingEnabled);
            Assert.IsTrue(settings.IsAlbumArtworkEnabled);
            await Task.WhenAll(settings.SetPlayReportingEnabledAsync(true),
                settings.SetAlbumArtworkEnabledAsync(false));
            await settings.FlushAsync();
            var reloaded = new RadioPrivacySettings(file);
            Assert.IsTrue(reloaded.IsPlayReportingEnabled);
            Assert.IsFalse(reloaded.IsAlbumArtworkEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DamagedOrIncompleteSettingsFailClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(file, "{}");
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
            Assert.IsFalse(new RadioPrivacySettings(file).IsAlbumArtworkEnabled);
            File.WriteAllText(file, "not json");
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
            Assert.IsFalse(new RadioPrivacySettings(file).IsAlbumArtworkEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FailedSaveRestoresLastDurableChoiceAndAllowsRetry()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(file); // A directory cannot be replaced by the settings file.
        try
        {
            var settings = new RadioPrivacySettings(file);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => settings.SetPlayReportingEnabledAsync(false));
            Assert.AreEqual(RadioPrivacyChoices.Default, settings.Current);

            Directory.Delete(file);
            await settings.SetPlayReportingEnabledAsync(false);
            Assert.AreEqual(new RadioPrivacyChoices(false, true), settings.Current);
            Assert.AreEqual(settings.Current, new RadioPrivacySettings(file).Current);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FutureSchemaFailsClosedAndLegacySchemaUpgradesOnSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(file,
                """{"SchemaVersion":2,"PlayReportingEnabled":true,"AlbumArtworkEnabled":true}""");
            Assert.AreEqual(RadioPrivacyChoices.FailClosed, new RadioPrivacySettings(file).Current);

            await File.WriteAllTextAsync(file, """{"PlayReportingEnabled":false}""");
            var settings = new RadioPrivacySettings(file);
            Assert.AreEqual(new RadioPrivacyChoices(false, true), settings.Current);
            await settings.SetAlbumArtworkEnabledAsync(false);
            StringAssert.Contains(await File.ReadAllTextAsync(file), "\"SchemaVersion\":1", StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
