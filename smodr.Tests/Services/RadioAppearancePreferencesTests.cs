using smodr.Services;
using smodr.ViewModels;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioAppearancePreferencesTests
{
    [TestMethod]
    public async Task DefaultAcrylicAndQueuedChoicesPersistInOrder()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioAppearancePreferences(profile.FilePath);
        Assert.AreEqual(RadioWindowBackdrop.Acrylic, preferences.Current);
        await Task.WhenAll(preferences.SetAsync(RadioWindowBackdrop.Mica), preferences.SetAsync(RadioWindowBackdrop.Solid), preferences.SetAsync(RadioWindowBackdrop.Acrylic));
        await preferences.FlushAsync();
        Assert.AreEqual(RadioWindowBackdrop.Acrylic, new RadioAppearancePreferences(profile.FilePath).Current);
        await preferences.SetAsync(RadioWindowBackdrop.Solid);
        Assert.AreEqual(RadioWindowBackdrop.Solid, new RadioAppearancePreferences(profile.FilePath).Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => preferences.SetAsync((RadioWindowBackdrop)99));
    }

    [TestMethod]
    [DataRow("broken")]
    [DataRow("{\"SchemaVersion\":2,\"Backdrop\":\"Acrylic\"}")]
    [DataRow("{\"SchemaVersion\":1,\"Backdrop\":999}")]
    [DataRow("{\"SchemaVersion\":1}")]
    public async Task InvalidSettingsArePreservedWithSolidFallback(string contents)
    {
        using var profile = new TemporaryRadioProfile();
        await File.WriteAllTextAsync(profile.FilePath, contents);
        var preferences = new RadioAppearancePreferences(profile.FilePath);
        Assert.IsTrue(preferences.IsReadOnly);
        Assert.AreEqual(RadioWindowBackdrop.Solid, preferences.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => preferences.SetAsync(RadioWindowBackdrop.Mica));
        Assert.AreEqual(contents, await File.ReadAllTextAsync(profile.FilePath));
    }

    [TestMethod]
    public async Task FailedSaveRollsBackUiAndLaterChoiceAppliesAfterDurability()
    {
        using var profile = new TemporaryRadioProfile();
        Directory.CreateDirectory(profile.FilePath);
        var preferences = new RadioAppearancePreferences(profile.FilePath);
        var settings = new RadioSettingsViewModel(new RadioPrivacySettings(profile.FilePath + ".privacy"), appearance: preferences);
        await settings.SetBackdropAsync((int)RadioWindowBackdrop.Solid);
        Assert.IsTrue(settings.HasError);
        Assert.AreEqual((int)RadioWindowBackdrop.Acrylic, settings.SelectedBackdrop);
        Directory.Delete(profile.FilePath);
        await settings.SetBackdropAsync((int)RadioWindowBackdrop.Mica);
        await settings.SetBackdropAsync(-1);
        await settings.FlushAsync();
        Assert.IsFalse(settings.HasError);
        Assert.IsTrue(settings.CanEditAppearance);
        Assert.AreEqual((int)RadioWindowBackdrop.Mica, settings.SelectedBackdrop);
        Assert.AreEqual(RadioWindowBackdrop.Mica, new RadioAppearancePreferences(profile.FilePath).Current);
    }

    [TestMethod]
    public async Task OversizedAppearanceFileRemainsUntouchedAndDisabled()
    {
        using var profile = new TemporaryRadioProfile();
        await File.WriteAllTextAsync(profile.FilePath, new string('x', 64_001));
        var preferences = new RadioAppearancePreferences(profile.FilePath);
        var settings = new RadioSettingsViewModel(new RadioPrivacySettings(profile.FilePath + ".privacy"), appearance: preferences);
        Assert.IsFalse(settings.CanEditAppearance);
        Assert.AreEqual((int)RadioWindowBackdrop.Solid, settings.SelectedBackdrop);
        Assert.AreEqual(64_001L, new FileInfo(profile.FilePath).Length);
    }
}
