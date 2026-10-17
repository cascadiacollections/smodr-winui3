using smodr.Services;
using smodr.Tests.Fixtures;

namespace smodr.Tests.Services;

/// <summary>
/// On-disk compatibility of the app preference files. "-current" fixtures were written by the
/// reflection-based serializer that shipped before source generation.
/// </summary>
[TestClass]
public sealed class PreferencesFormatTests
{
    [TestMethod]
    public async Task AppearanceCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("appearance-current.json");

        var appearance = new RadioAppearancePreferences(path);
        Assert.AreEqual(RadioWindowBackdrop.Mica, appearance.Current);
        Assert.IsFalse(appearance.IsReadOnly);

        await appearance.SetAsync(RadioWindowBackdrop.Solid);
        await appearance.SetAsync(RadioWindowBackdrop.Mica);

        Assert.AreEqual(PersistenceFixture.Read("appearance-current.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public void AppearanceAcceptsNumericBackdrop()
    {
        using var fixture = new PersistenceFixture();

        var appearance = new RadioAppearancePreferences(fixture.Install("appearance-numeric-enum.json"));

        Assert.AreEqual(RadioWindowBackdrop.Solid, appearance.Current);
        Assert.IsFalse(appearance.IsReadOnly);
    }

    [TestMethod]
    public async Task PlaybackCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("playback-current.json");

        var playback = new RadioPlaybackPreferences(path);
        Assert.IsFalse(playback.IsReadOnly);
        Assert.AreEqual(
            new RadioPlaybackOptions(PrewarmStreams: true, Equalizer: RadioEqualizerPreset.Bass,
                ResumeAfterNetworkLoss: false),
            playback.Current);

        await playback.UpdateAsync(options => options with { JumpLists = true });
        await playback.UpdateAsync(options => options with { JumpLists = false });

        Assert.AreEqual(PersistenceFixture.Read("playback-current.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public void PlaybackPartialOptionsUseConstructorDefaults()
    {
        using var fixture = new PersistenceFixture();

        var playback = new RadioPlaybackPreferences(fixture.Install("playback-legacy-partial.json"));

        Assert.IsFalse(playback.IsReadOnly);
        Assert.AreEqual(
            new RadioPlaybackOptions(Equalizer: RadioEqualizerPreset.Treble, JumpLists: true,
                ResumeAfterNetworkLoss: true),
            playback.Current);
    }

    [TestMethod]
    public void PlaybackAcceptsNumericEqualizer()
    {
        using var fixture = new PersistenceFixture();

        var playback = new RadioPlaybackPreferences(fixture.Install("playback-numeric-enum.json"));

        Assert.AreEqual(RadioEqualizerPreset.Bass, playback.Current.Equalizer);
    }

    [TestMethod]
    public async Task PrivacyCurrentFormatRewritesByteForByte()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("privacy-current.json");

        var privacy = new RadioPrivacySettings(path);
        Assert.AreEqual(new RadioPrivacyChoices(false, true), privacy.Current);

        await privacy.SetPlayReportingEnabledAsync(true);
        await privacy.SetPlayReportingEnabledAsync(false);
        await privacy.FlushAsync();

        Assert.AreEqual(PersistenceFixture.Read("privacy-current.json"), await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task PrivacyUnversionedFileKeepsReportingChoiceAndEnablesArtwork()
    {
        using var fixture = new PersistenceFixture();
        var path = fixture.Install("privacy-legacy-unversioned.json");

        var privacy = new RadioPrivacySettings(path);
        Assert.AreEqual(new RadioPrivacyChoices(false, true), privacy.Current);

        await privacy.SetAlbumArtworkEnabledAsync(true);
        await privacy.SetPlayReportingEnabledAsync(true);
        await privacy.SetPlayReportingEnabledAsync(false);
        await privacy.FlushAsync();

        Assert.AreEqual(PersistenceFixture.Read("privacy-current.json"), await File.ReadAllTextAsync(path));
    }
}
