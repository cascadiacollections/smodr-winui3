using smodr.Services;
using smodr.ViewModels;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RadioPlaybackPreferencesTests
{
    [TestMethod]
    public async Task LegacySettingsUseExplicitResumeDefaultsAndNewChoicesAreDurable()
    {
        using var profile = new TemporaryRadioProfile();
        await File.WriteAllTextAsync(profile.FilePath,
            """{"SchemaVersion":1,"Options":{"PrewarmStreams":true,"LoopFinishedBroadcasts":false,"Equalizer":"Off","JumpLists":false}}""");
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        Assert.IsFalse(preferences.IsReadOnly);
        Assert.IsFalse(preferences.Current.ResumeAfterSleep);
        Assert.IsTrue(preferences.Current.ResumeAfterNetworkLoss);
        var settings = new RadioSettingsViewModel(new RadioPrivacySettings(profile.FilePath + ".privacy"),
            playback: preferences);
        await settings.SetResumeAfterSleepEnabledAsync(true);
        await settings.SetResumeAfterNetworkLossEnabledAsync(false);
        await settings.FlushAsync();
        var restored = new RadioPlaybackPreferences(profile.FilePath).Current;
        Assert.IsTrue(restored.ResumeAfterSleep);
        Assert.IsFalse(restored.ResumeAfterNetworkLoss);
        Assert.IsTrue(restored.PrewarmStreams);
    }

    [TestMethod]
    public async Task OptionalFeaturesDefaultOffAndConcurrentEditsAreDurable()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        Assert.AreEqual(new RadioPlaybackOptions(), preferences.Current);
        var writes = new[]
        {
            preferences.UpdateAsync(value => value with { PrewarmStreams = true }),
            preferences.UpdateAsync(value => value with { LoopFinishedBroadcasts = true }),
            preferences.UpdateAsync(value => value with { Equalizer = RadioEqualizerPreset.Speech })
        };
        await Task.WhenAll(writes);
        await preferences.FlushAsync();
        var restored = new RadioPlaybackPreferences(profile.FilePath);
        Assert.AreEqual(new RadioPlaybackOptions(true, true, RadioEqualizerPreset.Speech), restored.Current);
    }

    [TestMethod]
    public async Task FailedWriteDoesNotPublishAndNextWriteCanSucceed()
    {
        using var profile = new TemporaryRadioProfile();
        Directory.CreateDirectory(profile.FilePath);
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            preferences.UpdateAsync(value => value with { PrewarmStreams = true }));
        Assert.AreEqual(new RadioPlaybackOptions(), preferences.Current);
        Directory.Delete(profile.FilePath);
        await preferences.UpdateAsync(value => value with { Equalizer = RadioEqualizerPreset.Bass });
        Assert.AreEqual(RadioEqualizerPreset.Bass, new RadioPlaybackPreferences(profile.FilePath).Current.Equalizer);
    }

    [TestMethod]
    [DataRow("{\"SchemaVersion\":99,\"Options\":{\"PrewarmStreams\":true}}")]
    [DataRow("{\"SchemaVersion\":1,\"Options\":{\"Equalizer\":99}}")]
    [DataRow("{\"SchemaVersion\":1,\"Options\":null}")]
    [DataRow("broken json")]
    public async Task UnreadableOrFutureSettingsArePreservedAndFailClosed(string contents)
    {
        using var profile = new TemporaryRadioProfile();
        await File.WriteAllTextAsync(profile.FilePath, contents);
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        Assert.IsTrue(preferences.IsReadOnly);
        Assert.AreEqual(new RadioPlaybackOptions(), preferences.Current);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            preferences.UpdateAsync(value => value with { PrewarmStreams = true }));
        Assert.AreEqual(contents, await File.ReadAllTextAsync(profile.FilePath));
    }

    [TestMethod]
    public async Task SettingsExposeDurablePlaybackChoicesAndIgnoreInvalidPresets()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        var cleared = 0;
        var settings = new RadioSettingsViewModel(new RadioPrivacySettings(profile.FilePath + ".privacy"),
            playback: preferences, clearWarmup: () => cleared++);
        await settings.SetStreamPrewarmingEnabledAsync(true);
        await settings.SetLoopFinishedBroadcastsEnabledAsync(true);
        await settings.SetEqualizerPresetAsync((int)RadioEqualizerPreset.Treble);
        await settings.SetEqualizerPresetAsync(999);
        await settings.FlushAsync();
        Assert.IsTrue(settings.IsStreamPrewarmingEnabled);
        Assert.IsTrue(settings.IsLoopFinishedBroadcastsEnabled);
        Assert.AreEqual(3, settings.SelectedEqualizerPreset);
        Assert.AreEqual(2, cleared);
        Assert.IsTrue(settings.CanEditPlayback);
        Assert.IsFalse(settings.HasError);
    }

    [TestMethod]
    public async Task JumpListChoiceIsOptInCapabilityGatedAndRefreshesAfterPersistence()
    {
        using var profile = new TemporaryRadioProfile();
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        var privacy = new RadioPrivacySettings(profile.FilePath + ".privacy");
        var unsupported = new RadioSettingsViewModel(privacy, playback: preferences);
        Assert.IsFalse(unsupported.CanEditJumpLists);
        Assert.IsFalse(unsupported.IsJumpListEnabled);
        var refreshes = 0;
        var settings = new RadioSettingsViewModel(privacy, playback: preferences, jumpListSupported: true,
            refreshJumpList: () =>
            {
                Assert.IsTrue(new RadioPlaybackPreferences(profile.FilePath).Current.JumpLists);
                refreshes++;
            });
        Assert.IsTrue(settings.CanEditJumpLists);
        await settings.SetJumpListEnabledAsync(true);
        await settings.FlushAsync();
        Assert.IsTrue(settings.IsJumpListEnabled);
        Assert.AreEqual(1, refreshes);
    }

    [TestMethod]
    public void EqualizerProfilesHaveBoundedBoostAndCompensatingHeadroom()
    {
        foreach (var preset in Enum.GetValues<RadioEqualizerPreset>())
        {
            var bands = RadioEqualizerProfiles.Bands(preset);
            Assert.HasCount(4, bands);
            Assert.IsTrue(bands.All(band =>
                band.Frequency is > 0 and < 20000 && double.IsFinite(band.Gain) && band.Gain > 0));
            Assert.IsTrue(bands.Max(band => band.Gain) * RadioEqualizerProfiles.Headroom(preset) <= 1.000001);
        }
    }

    [TestMethod]
    public async Task OversizedSettingsRemainUntouchedAndDisableOnlyPlaybackEdits()
    {
        using var profile = new TemporaryRadioProfile();
        await File.WriteAllBytesAsync(profile.FilePath, new byte[64_001]);
        var preferences = new RadioPlaybackPreferences(profile.FilePath);
        var settings = new RadioSettingsViewModel(new RadioPrivacySettings(profile.FilePath + ".privacy"),
            playback: preferences);
        Assert.IsTrue(preferences.IsReadOnly);
        Assert.IsFalse(settings.CanEditPlayback);
        Assert.IsTrue(settings.CanEdit);
        await settings.SetLoopFinishedBroadcastsEnabledAsync(true);
        Assert.IsTrue(settings.HasError);
        Assert.IsFalse(settings.IsLoopFinishedBroadcastsEnabled);
        Assert.AreEqual(64_001L, new FileInfo(profile.FilePath).Length);
    }

    [TestMethod]
    [DataRow(false, true, 180, false)]
    [DataRow(true, false, 180, false)]
    [DataRow(true, true, 0, false)]
    [DataRow(true, true, 180, true)]
    public void LoopRequiresOptInRequestedPlaybackAndKnownDuration(bool enabled, bool requested, int duration,
        bool expected)
    {
        Assert.AreEqual(expected,
            FinishedBroadcastPolicy.ShouldLoop(enabled, requested, TimeSpan.FromSeconds(duration)));
    }

    [TestMethod]
    public void UnknownInfiniteDurationDoesNotLoop()
    {
        Assert.IsFalse(FinishedBroadcastPolicy.ShouldLoop(true, true, TimeSpan.MaxValue));
    }
}

internal sealed class TemporaryRadioProfile : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "shoutkit-options-" + Guid.NewGuid().ToString("N"));

    public TemporaryRadioProfile()
    {
        Directory.CreateDirectory(_root);
    }

    public string FilePath => Path.Combine(_root, "settings.json");

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(_root);
        if (!Path.GetDirectoryName(fullPath)!.Equals(
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("shoutkit-options-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected test profile path.");
        }

        Directory.Delete(fullPath, true);
        GC.SuppressFinalize(this);
    }
}
