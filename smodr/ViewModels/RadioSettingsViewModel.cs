using CommunityToolkit.Mvvm.ComponentModel;
using smodr.Services;

namespace smodr.ViewModels;

/// <summary>UI-thread settings presentation; persistence remains in the shared privacy service.</summary>
public partial class RadioSettingsViewModel(IRadioPrivacySettings privacy,
    Func<bool, Task>? setArtwork = null, Func<Task<string>>? readLicenses = null) : ObservableObject
{
    private readonly IRadioPrivacySettings _privacy = privacy;
    private readonly Func<bool, Task> _setArtwork = setArtwork ?? privacy.SetAlbumArtworkEnabledAsync;
    private readonly Func<Task<string>> _readLicenses = readLicenses ?? (() => File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Assets", "SoftwareLicenses.txt")));

    public bool IsPlayReportingEnabled => _privacy.IsPlayReportingEnabled;
    public bool IsAlbumArtworkEnabled => _privacy.IsAlbumArtworkEnabled;
    public bool CanEdit => !IsSaving;
    public bool HasError => ErrorMessage.Length != 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
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

    private async Task SaveAsync(bool enabled, bool current, Func<bool, Task> save,
        string category, string choice)
    {
        // Toggle notifications caused by initialization or rollback are not edits.
        // Both controls are disabled during an edit; ignore duplicate/reentrant calls.
        if (IsSaving || enabled == current) return;
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
