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
            await settings.SetPlayReportingEnabledAsync(false);
            await settings.FlushAsync();
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
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
    public void DamagedOrIncompleteSettingsFailClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoutkit-privacy-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(file, "{}");
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
            File.WriteAllText(file, "not json");
            Assert.IsFalse(new RadioPrivacySettings(file).IsPlayReportingEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
