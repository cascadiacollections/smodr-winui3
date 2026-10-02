using smodr.Services;
using smodr.ViewModels;

namespace smodr.Tests.ViewModels;

[TestClass]
public sealed class RadioSettingsViewModelTests
{
    [TestMethod]
    public async Task InitialValuesAndUnchangedNotificationsDoNotWrite()
    {
        var privacy = new StubPrivacy();
        var settings = new RadioSettingsViewModel(privacy);
        Assert.IsTrue(settings.IsPlayReportingEnabled);
        Assert.IsTrue(settings.IsAlbumArtworkEnabled);
        Assert.IsTrue(settings.CanEdit);
        await settings.SetPlayReportingEnabledAsync(true);
        await settings.SetAlbumArtworkEnabledAsync(true);
        Assert.AreEqual(0, privacy.Writes);
    }

    [TestMethod]
    public async Task PendingEditDisablesBothControlsAndIgnoresDuplicates()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var privacy = new StubPrivacy { BeforeSave = () => completion.Task };
        var settings = new RadioSettingsViewModel(privacy);
        var pending = settings.SetPlayReportingEnabledAsync(false);
        Assert.IsTrue(settings.IsSaving);
        Assert.IsFalse(settings.CanEdit);
        await settings.SetPlayReportingEnabledAsync(false);
        await settings.SetAlbumArtworkEnabledAsync(false);
        Assert.AreEqual(1, privacy.Writes);
        completion.SetResult();
        await pending;
        Assert.IsFalse(settings.IsPlayReportingEnabled);
        Assert.IsTrue(settings.IsAlbumArtworkEnabled);
        Assert.IsTrue(settings.CanEdit);
    }

    [TestMethod]
    public async Task FailureRestoresDurableChoiceAndNextSuccessfulEditClearsError()
    {
        var privacy = new StubPrivacy
        {
            BeforeSave = () => Task.FromException(new IOException("Synthetic save failure"))
        };
        var settings = new RadioSettingsViewModel(privacy);
        var restoredWhileBusy = false;
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(settings.IsPlayReportingEnabled))
                restoredWhileBusy = settings.IsSaving;
        };
        await settings.SetPlayReportingEnabledAsync(false);
        Assert.IsTrue(settings.IsPlayReportingEnabled);
        Assert.IsTrue(restoredWhileBusy);
        Assert.IsTrue(settings.HasError);
        StringAssert.Contains(settings.ErrorMessage, "play-reporting", StringComparison.Ordinal);
        Assert.IsTrue(settings.CanEdit);
        privacy.BeforeSave = () => Task.CompletedTask;
        await settings.SetPlayReportingEnabledAsync(false);
        Assert.IsFalse(settings.IsPlayReportingEnabled);
        Assert.IsFalse(settings.HasError);
    }

    [TestMethod]
    public async Task ArtworkUsesPlaybackAwareSetterAndRecoversAfterFailure()
    {
        var privacy = new StubPrivacy();
        var refreshes = 0;
        var settings = new RadioSettingsViewModel(privacy, async enabled =>
        {
            try { await privacy.SetAlbumArtworkEnabledAsync(enabled); }
            finally { refreshes++; }
        });
        privacy.BeforeSave = () => Task.FromException(new IOException("Synthetic save failure"));
        await settings.SetAlbumArtworkEnabledAsync(false);
        Assert.IsTrue(settings.IsAlbumArtworkEnabled);
        StringAssert.Contains(settings.ErrorMessage, "album-artwork", StringComparison.Ordinal);
        privacy.BeforeSave = () => Task.CompletedTask;
        await settings.SetAlbumArtworkEnabledAsync(false);
        Assert.IsFalse(settings.IsAlbumArtworkEnabled);
        Assert.AreEqual(2, refreshes);
        Assert.IsFalse(settings.HasError);
    }

    [TestMethod]
    public async Task RollbackNotificationsCannotTriggerAdditionalWrites()
    {
        var privacy = new StubPrivacy();
        var settings = new RadioSettingsViewModel(privacy);
        Task? reentrant = null;
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(settings.IsAlbumArtworkEnabled))
                reentrant = settings.SetAlbumArtworkEnabledAsync(false);
        };
        await settings.SetPlayReportingEnabledAsync(false);
        await reentrant!;
        Assert.AreEqual(1, privacy.Writes);
        Assert.IsTrue(settings.IsAlbumArtworkEnabled);
    }

    [TestMethod]
    public async Task LicenseReadSupportsOfflineContentAndSafeFailureMessage()
    {
        var settings = new RadioSettingsViewModel(new StubPrivacy(),
            readLicenses: () => Task.FromResult("Bundled notices"));
        Assert.AreEqual("Bundled notices", await settings.LoadSoftwareLicensesAsync());
        settings = new RadioSettingsViewModel(new StubPrivacy(),
            readLicenses: () => Task.FromException<string>(new IOException("Synthetic read failure")));
        StringAssert.Contains(await settings.LoadSoftwareLicensesAsync(), "unavailable", StringComparison.Ordinal);
        Assert.IsFalse(settings.HasError);
    }

    private sealed class StubPrivacy : IRadioPrivacySettings
    {
        public Func<Task> BeforeSave { get; set; } = () => Task.CompletedTask;
        public int Writes { get; private set; }
        public RadioPrivacyChoices Current { get; private set; } = new(true, true);
        public bool IsPlayReportingEnabled => Current.PlayReportingEnabled;
        public bool IsAlbumArtworkEnabled => Current.AlbumArtworkEnabled;
        public async Task SetPlayReportingEnabledAsync(bool enabled)
        {
            Writes++;
            await BeforeSave();
            Current = Current with { PlayReportingEnabled = enabled };
        }
        public async Task SetAlbumArtworkEnabledAsync(bool enabled)
        {
            Writes++;
            await BeforeSave();
            Current = Current with { AlbumArtworkEnabled = enabled };
        }
        public Task FlushAsync() => Task.CompletedTask;
    }
}
