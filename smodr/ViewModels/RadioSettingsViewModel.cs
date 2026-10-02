using CommunityToolkit.Mvvm.ComponentModel;
using smodr.Services;

namespace smodr.ViewModels;

/// <summary>UI-thread settings presentation; persistence remains in the shared privacy service.</summary>
public partial class RadioSettingsViewModel(IRadioPrivacySettings privacy,
    Func<bool, Task>? setArtwork = null, Func<Task<string>>? readLicenses = null,
    RadioPlaybackPreferences? playback = null, Action? clearWarmup = null,
    bool jumpListSupported = false, Action? refreshJumpList = null) : ObservableObject
{
    private Task _pendingEdit = Task.CompletedTask;
    private readonly IRadioPrivacySettings _privacy = privacy;
    private readonly Func<bool, Task> _setArtwork = setArtwork ?? privacy.SetAlbumArtworkEnabledAsync;
    private readonly Func<Task<string>> _readLicenses = readLicenses ?? (() => File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Assets", "SoftwareLicenses.txt")));

    public bool IsPlayReportingEnabled => _privacy.IsPlayReportingEnabled;
    public bool IsAlbumArtworkEnabled => _privacy.IsAlbumArtworkEnabled;
    public bool CanEdit => !IsSaving;
    public bool CanEditPlayback => CanEdit && playback is { IsReadOnly: false };
    public bool CanEditJumpLists => CanEditPlayback && jumpListSupported;
    public bool IsJumpListEnabled => playback?.Current.JumpLists == true;
    public bool IsStreamPrewarmingEnabled => playback?.Current.PrewarmStreams == true;
    public bool IsLoopFinishedBroadcastsEnabled => playback?.Current.LoopFinishedBroadcasts == true;
    public int SelectedEqualizerPreset => (int)(playback?.Current.Equalizer ?? RadioEqualizerPreset.Off);
    public bool HasError => ErrorMessage.Length != 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanEditPlayback))]
    [NotifyPropertyChangedFor(nameof(CanEditJumpLists))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public Task SetPlayReportingEnabledAsync(bool enabled) =>
        SaveAsync(enabled, IsPlayReportingEnabled, _privacy.SetPlayReportingEnabledAsync,
            "privacy.write", "play-reporting");

    public Task SetAlbumArtworkEnabledAsync(bool enabled) =>
        SaveAsync(enabled, IsAlbumArtworkEnabled, _setArtwork,
            "privacy.artwork-write", "album-artwork");

    public Task SetStreamPrewarmingEnabledAsync(bool enabled) =>
        SaveAsync(enabled, IsStreamPrewarmingEnabled, async value =>
        {
            if (playback is null) return;
            await playback.UpdateAsync(options => options with { PrewarmStreams = value });
            clearWarmup?.Invoke();
        }, "playback.prewarm-write", "stream-prewarming");

    public Task SetLoopFinishedBroadcastsEnabledAsync(bool enabled) =>
        SaveAsync(enabled, IsLoopFinishedBroadcastsEnabled,
            value => playback?.UpdateAsync(options => options with { LoopFinishedBroadcasts = value }) ?? Task.CompletedTask,
            "playback.loop-write", "broadcast-looping");

    public Task SetEqualizerPresetAsync(int index)
    {
        if (!Enum.IsDefined((RadioEqualizerPreset)index) || playback is null) return Task.CompletedTask;
        return SaveAsync(index, SelectedEqualizerPreset, async value =>
        {
            await playback.UpdateAsync(options => options with { Equalizer = (RadioEqualizerPreset)value });
            clearWarmup?.Invoke();
        }, "playback.equalizer-write", "equalizer");
    }

    public Task FlushAsync() => Task.WhenAll(_pendingEdit, playback?.FlushAsync() ?? Task.CompletedTask);

    public Task SetJumpListEnabledAsync(bool enabled) =>
        SaveAsync(enabled, IsJumpListEnabled, async value =>
        {
            if (playback is null) return;
            await playback.UpdateAsync(options => options with { JumpLists = value });
            refreshJumpList?.Invoke();
        }, "shell.jump-list-choice", "jump-list");

    private Task SaveAsync<T>(T enabled, T current, Func<T, Task> save, string category, string choice)
    {
        if (IsSaving || EqualityComparer<T>.Default.Equals(enabled, current)) return Task.CompletedTask;
        _pendingEdit = SaveCoreAsync(enabled, save, category, choice);
        return _pendingEdit;
    }

    private async Task SaveCoreAsync<T>(T enabled, Func<T, Task> save,
        string category, string choice)
    {
        // Toggle notifications caused by initialization or rollback are not edits.
        // Both controls are disabled during an edit; ignore duplicate/reentrant calls.
        IsSaving = true;
        ErrorMessage = string.Empty;
        try { await save(enabled); }
        catch (Exception exception)
        {
            AppDiagnostics.Record(category, exception);
            ErrorMessage = $"Your {choice} choice could not be saved. The previous choice was restored.";
        }
        finally
        {
            // Notify while still busy so restoring durable values cannot cause writes.
            OnPropertyChanged(nameof(IsPlayReportingEnabled));
            OnPropertyChanged(nameof(IsAlbumArtworkEnabled));
            OnPropertyChanged(nameof(IsStreamPrewarmingEnabled));
            OnPropertyChanged(nameof(IsLoopFinishedBroadcastsEnabled));
            OnPropertyChanged(nameof(SelectedEqualizerPreset));
            OnPropertyChanged(nameof(IsJumpListEnabled));
            IsSaving = false;
        }
    }

    public async Task<string> LoadSoftwareLicensesAsync()
    {
        try { return await _readLicenses(); }
        catch (Exception exception)
        {
            AppDiagnostics.Record("licenses.read", exception);
            return "Software license notices are unavailable in this installation. See LICENSE.txt and the package lockfile in the project repository.";
        }
    }
}
